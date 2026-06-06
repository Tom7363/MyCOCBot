Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.IO
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client

Public Module OracleDatabaseManager

    Private conn As OracleConnection = Nothing
    ''' <summary>
    ''' Reads user, password, and data source from a configuration text file and opens the connection asynchronously.
    ''' </summary>
    ''' 

    Private ConnectionString As String
    Public Async Function ConnectDBAsync() As Task(Of OracleConnection)
        If conn Is Nothing Then
            conn = Await GetCloudConnectionAsync()
            Return conn
        End If
        Return Nothing
    End Function

    Public Function IsDBConnected() As Boolean
        ' Prüfen, ob das Objekt existiert und ob der Status auf "Open" steht
        If conn IsNot Nothing AndAlso conn.State = System.Data.ConnectionState.Open Then
            Return True
        Else
            Return False
        End If
    End Function

    Public Async Function KeepDatabaseAliveAsync() As Task
        Dim sql As String = "SELECT 1 FROM DUAL"

        Await conn.OpenAsync()
        Using cmd As New OracleCommand(sql, conn)
            Await cmd.ExecuteScalarAsync()
        End Using
    End Function
    Public Async Function GetCloudConnectionAsync() As Task(Of OracleConnection)
        Try
            ' 1. Define paths (files are located directly next to the .exe)
            Dim baseFolder As String = AppContext.BaseDirectory
            Dim walletFolder As String = Path.Combine(baseFolder, "wallet")
            Dim configFilePfad As String = Path.Combine(baseFolder, "config", "dbconfig.txt")

            ' Check if the configuration file exists
            If Not File.Exists(configFilePfad) Then
                Console.WriteLine($"[DB-ERROR] The file 'dbconfig.txt' is missing in folder: {baseFolder}")
                Return Nothing
            End If

            ' 2. Read the text file line by line
            Dim lines As String() = File.ReadAllLines(configFilePfad)

            ' Validation: The file must contain at least 3 lines
            If lines.Length < 3 Then
                Console.WriteLine("[DB-ERROR] 'dbconfig.txt' must use the following format: Line 1: User, Line 2: Password, Line 3: Data Source")
                Return Nothing
            End If

            ' Extract data (TRIM removes accidental whitespace at the end of lines)
            Dim dbUser As String = lines(0).Trim()
            Dim dbPassword As String = lines(1).Trim()
            Dim dataSource As String = lines(2).Trim()

            ' 3. Assign the certificates to the Oracle driver
            If Not Directory.Exists(walletFolder) Then
                Console.WriteLine($"[DB-ERROR] The Wallet folder is missing at: {walletFolder}")
                Return Nothing
            End If

            OracleConfiguration.TnsAdmin = walletFolder
            OracleConfiguration.WalletLocation = walletFolder

            ' 4. Assemble the connection string dynamically
            Dim connString As String = $"User Id={dbUser};Password={dbPassword};Data Source={dataSource};"
            ConnectionString = connString
            ' 5. Open the connection asynchronously
            Dim conn As New OracleConnection(connString)
            Await conn.OpenAsync()

            Return conn

        Catch ex As Exception
            ' Linux-safe console logging instead of Windows MsgBox
            Console.WriteLine($"[DB-CRITICAL] Error reading login data or establishing connection: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Logs a command execution entry into the Oracle Cloud DB for every executed slash command.
    ''' </summary>
    Public Async Function LogCommandAsync(userName As String, guildName As String, commandName As String, statusMessage As String) As Task
        Await Task.Run(
            Async Function()
                If conn Is Nothing Then
                    conn = Await GetCloudConnectionAsync()

                End If
                If conn Is Nothing Then Return

                Using conn
                    Dim sql As String = "INSERT INTO bot_logs (user_name, guild_name, command_used, status_msg) VALUES (:usr, :gld, :cmd, :stat)"
                    Using cmd As New OracleCommand(sql, conn)
                        cmd.Parameters.Add(New OracleParameter("usr", userName))
                        cmd.Parameters.Add(New OracleParameter("gld", guildName))
                        cmd.Parameters.Add(New OracleParameter("cmd", commandName))
                        cmd.Parameters.Add(New OracleParameter("stat", statusMessage))

                        Try
                            Await cmd.ExecuteNonQueryAsync()
                        Catch ex As Exception
                            Console.WriteLine($"[DB-ERROR] Failed to write bot log: {ex.Message}")
                        End Try
                    End Using
                End Using
            End Function)
    End Function

    ''' <summary>
    ''' Retrieves the last 5 command log entries from the database.
    ''' </summary>
    Public Async Function GetLastFiveLogsAsync() As Task(Of List(Of String))
        Return Await Task.Run(
            Async Function()
                Dim logs As New List(Of String)()
                If conn Is Nothing Then
                    conn = Await GetCloudConnectionAsync()

                End If
                If conn Is Nothing Then
                    logs.Add("❌ Connection to Oracle Cloud failed.")
                    Return logs
                End If

                Using conn
                    Dim sql As String = "SELECT TO_CHAR(log_date, 'HH24:MI:SS'), user_name, command_used FROM bot_logs ORDER BY log_id DESC FETCH FIRST 5 ROWS ONLY"
                    Using cmd As New OracleCommand(sql, conn)
                        Try
                            Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                                While Await reader.ReadAsync()
                                    Dim entry As String = $"`[{reader.GetString(0)}]` **{reader.GetString(1)}** used `/{reader.GetString(2)}`"
                                    logs.Add(entry)
                                End While
                            End Using
                        Catch ex As Exception
                            logs.Add($"❌ Query error: {ex.Message}")
                        End Try
                    End Using
                End Using

                If logs.Count = 0 Then logs.Add("ℹ️ No entries found in the log table.")
                Return logs
            End Function)
    End Function

    ''' <summary>
    ''' Retrieves all registered clans from the 'clans' table (for /showclans).
    ''' </summary>
    Public Async Function GetClansAsync() As Task(Of List(Of String))
        Dim clanList As New List(Of String)()

        ' 1. Check if the Supercell API token has been initialized
        If String.IsNullOrEmpty(CocService.apiToken) Then
            clanList.Add("❌ Supercell API Token is missing or not initialized yet.")
            Return clanList
        End If

        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

        Try
            ' 2. MANAGING THE GLOBAL CONNECTION (Crucial Step!)
            ' If the global variable is null, initialize it for the first time
            If conn Is Nothing Then
                Return Nothing
            End If

            ' If the connection exist but was closed by a network timeout, re-open it
            If conn IsNot Nothing AndAlso conn.State = System.Data.ConnectionState.Closed Then
                Await conn.OpenAsync()
            End If

            ' Backup safety check
            If conn Is Nothing OrElse conn.State <> System.Data.ConnectionState.Open Then
                clanList.Add("❌ Global connection to Oracle Cloud is offline.")
                Return clanList
            End If

            ' 3. RUN DATABASE QUERY
            Dim sql As String = "SELECT clan_name, clan_tag, category FROM clan_export ORDER BY clan_name ASC"

            ' WICHTIG: KEIN 'Using conn' hier! Das würde die globale Verbindung zerstören.
            ' Wir nutzen das Using NUR für das Command und den Reader.
            Using cmd As New OracleCommand(sql, conn)
                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)

                    Dim counter As Integer = 0

                    While Await reader.ReadAsync()
                        counter += 1
                        Dim name As String = reader.GetString(0)
                        Dim tag As String = reader.GetString(1)
                        Dim category As String = reader.GetString(2)

                        ' 4. FETCH SUPERCELL LIVE DATA
                        Dim liveMembers As Integer = Await cocApi.GetMemberCountAsync(tag)

                        ' 5. Format the output row
                        If liveMembers >= 0 Then
                            clanList.Add($"{counter}. **{name}** (`{tag}`) - [{category}] | **{liveMembers}/50** members")

                            ' BACKWARD SYNC: Update the live numbers back into the database
                            Try
                                Dim updateSql As String = "UPDATE clan_export SET members_count = :count WHERE clan_tag = :tag"
                                Using updateCmd As New OracleCommand(updateSql, conn)
                                    updateCmd.Parameters.Add(New OracleParameter("count", liveMembers))
                                    updateCmd.Parameters.Add(New OracleParameter("tag", tag))
                                    Await updateCmd.ExecuteNonQueryAsync()

                                    ' Execute the mandatory Oracle COMMIT to save changes permanently
                                    Using commitCmd As New OracleCommand("COMMIT", conn)
                                        Await commitCmd.ExecuteNonQueryAsync()
                                    End Using
                                End Using
                            Catch updateEx As Exception
                                Console.WriteLine($"[DB-SYNC-WARNING] Failed to update cache for {tag}: {updateEx.Message}")
                            End Try
                        Else
                            clanList.Add($"{counter}. **{name}** (`{tag}`) - [{category}] | ⚠️ *Live API Request Failed*")
                        End If
                    End While

                End Using
            End Using

        Catch ex As Exception
            Console.WriteLine($"[DB-ERROR] Critical failure during global connection processing: {ex.Message}")
            clanList.Add($"❌ Critical database error: {ex.Message}")
        End Try

        ' 6. Fallback check if the database was empty
        If clanList.Count = 0 Then
            clanList.Add("ℹ️ No registered clans found in the database directory.")
        End If

        Return clanList
    End Function

    Public Async Function GetDatabaseStatsAsync() As Task(Of Dictionary(Of String, String))
        Dim stats As New Dictionary(Of String, String) From {
            {"SizeMB", "N/A"},
            {"Sessions", "N/A"}
        }

        Try
            If conn IsNot Nothing AndAlso conn.State = System.Data.ConnectionState.Closed Then
                Await conn.OpenAsync()
            End If

            ' Query 1: Get total space allocated by user segments in Megabytes
            Dim sizeQuery As String = "SELECT NVL(SUM(bytes) / 1024 / 1024, 0) FROM user_segments"
            Using cmdSize As New OracleCommand(sizeQuery, conn)
                Dim sizeResult = Await cmdSize.ExecuteScalarAsync()
                If sizeResult IsNot DBNull.Value Then
                    stats("SizeMB") = Convert.ToDouble(sizeResult).ToString("F2") & " MB"
                End If
            End Using

            ' Query 2: Get active user sessions for your connection pool limit
            Dim sessionQuery As String = "SELECT COUNT(*) FROM v$session WHERE username = USER"
            Using cmdSessions As New OracleCommand(sessionQuery, conn)
                Dim sessionResult = Await cmdSessions.ExecuteScalarAsync()
                If sessionResult IsNot DBNull.Value Then
                    stats("Sessions") = sessionResult.ToString()
                End If
            End Using
        Catch ex As Exception
            ' Fallback if views are restricted or connection drops
            stats("SizeMB") = "Error loading"
            stats("Sessions") = "Error loading"
        End Try

        Return stats
    End Function



    ''' <summary>
    ''' Upserts a clan tracking record along with its operational choice category.
    ''' </summary>
    Public Async Function AddClanAsync(guildId As ULong, clanTag As String, clanName As String, clanCategory As String) As Task(Of Boolean)
        Dim query As String = "MERGE INTO tracked_clans t USING " &
                             "(SELECT :guildId as g_id, :clanTag as c_tag FROM dual) src " &
                             "ON (t.guild_id = src.g_id AND t.clan_tag = src.c_tag) " &
                             "WHEN NOT MATCHED THEN INSERT (guild_id, clan_tag, clan_name, clan_category, registered_at) " &
                             "VALUES (src.g_id, src.c_tag, :clanName, :clanCategory, SYSDATE) " &
                             "WHEN MATCHED THEN UPDATE SET t.clan_category = :clanCategory"
        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Int64)).Value = Convert.ToInt64(guildId)
                cmd.Parameters.Add(New OracleParameter("clanTag", OracleDbType.Varchar2)).Value = clanTag.ToUpper()
                cmd.Parameters.Add(New OracleParameter("clanName", OracleDbType.Varchar2)).Value = clanName
                cmd.Parameters.Add(New OracleParameter("clanCategory", OracleDbType.Varchar2)).Value = clanCategory

                Await cmd.ExecuteNonQueryAsync()
                Return True
            End Using

        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] AddClanAsync failed: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Deletes a specific tracked clan within a unique Discord server context.
    ''' </summary>
    Public Async Function RemoveClanAsync(guildId As ULong, clanTag As String) As Task(Of Boolean)
        Dim query As String = "DELETE FROM tracked_clans WHERE guild_id = :guildId AND UPPER(clan_tag) = :clanTag"
        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Int64)).Value = Convert.ToInt64(guildId)
                cmd.Parameters.Add(New OracleParameter("clanTag", OracleDbType.Varchar2)).Value = clanTag.ToUpper()

                Dim rowsAffected As Integer = Await cmd.ExecuteNonQueryAsync()
                Return rowsAffected > 0
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] RemoveClanAsync failed: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Returns all clans registered to a specific server guild, sorted by classification.
    ''' </summary>
    Public Async Function GetClansAsync(guildId As ULong) As Task(Of List(Of Tuple(Of String, String, String)))
        Dim clansList As New List(Of Tuple(Of String, String, String))()
        Dim query As String = "SELECT clan_tag, clan_name, NVL(clan_category, 'Unassigned') FROM tracked_clans WHERE guild_id = :guildId ORDER BY clan_category, clan_name"

        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Int64)).Value = Convert.ToInt64(guildId)
                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                    While Await reader.ReadAsync()
                        clansList.Add(Tuple.Create(reader.GetString(0), reader.GetString(1), reader.GetString(2)))
                    End While
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetClansAsync failed: {ex.Message}")
        End Try
        Return clansList
    End Function

    ''' <summary>
    ''' Ruft alle Clans aus der Tabelle tracked_clans ab, bei denen das Feld DUMP den Wert '1' hat.
    ''' </summary>
    Public Async Function GetDumpClansAsync(guildId As ULong) As Task(Of List(Of Dictionary(Of String, String)))
        Dim clanList As New List(Of Dictionary(Of String, String))()

        ' Die SQL-Query filtert hier explizit nach DUMP = '1'
        Dim query As String = "SELECT clan_tag, clan_name, NVL(clan_category, 'Unassigned'), DUMP " &
                          "FROM tracked_clans " &
                          "WHERE guild_id = :guildId AND DUMP = '1' " &
                          "ORDER BY clan_category, clan_name"

        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Varchar2)).Value = guildId.ToString()

                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                    While Await reader.ReadAsync()
                        Dim clanData As New Dictionary(Of String, String)()
                        clanData("tag") = reader.GetString(0)
                        clanData("name") = reader.GetString(1)
                        clanData("category") = reader.GetString(2)
                        clanData("dump") = reader.GetString(3)
                        clanList.Add(clanData)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetDumpClansAsync failed: {ex.Message}")
        End Try

        Return clanList
    End Function



    '' <summary>
    ''' Saves a new base layout with a name, links, and notes to the Oracle DB.
    ''' </summary>
    Public Async Function AddBaseLayoutAsync(guildId As ULong, name As String, cocLink1 As String, cocLink2 As String, imageLink As String, layoutInfo As String) As Task(Of Boolean)
        Dim query As String = "INSERT INTO base_layouts (guild_id, layout_name, coc_link, coc_link_2, image_link, layout_info) " &
                         "VALUES (:guildId, :layoutName, :cocLink1, :cocLink2, :imageLink, :layoutInfo)"
        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Int64)).Value = Convert.ToInt64(guildId)
                cmd.Parameters.Add(New OracleParameter("layoutName", OracleDbType.Varchar2)).Value = name
                cmd.Parameters.Add(New OracleParameter("cocLink1", OracleDbType.Varchar2)).Value = cocLink1

                Dim pLink2 As New OracleParameter("cocLink2", OracleDbType.Varchar2)
                pLink2.Value = If(String.IsNullOrEmpty(cocLink2), DBNull.Value, cocLink2)
                cmd.Parameters.Add(pLink2)

                Dim pImage As New OracleParameter("imageLink", OracleDbType.Varchar2)
                pImage.Value = If(String.IsNullOrEmpty(imageLink), DBNull.Value, imageLink)
                cmd.Parameters.Add(pImage)

                ' Handle optional information text cleanly
                Dim pInfo As New OracleParameter("layoutInfo", OracleDbType.Varchar2)
                pInfo.Value = If(String.IsNullOrEmpty(layoutInfo), DBNull.Value, layoutInfo)
                cmd.Parameters.Add(pInfo)

                Await cmd.ExecuteNonQueryAsync()
                Return True
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] AddBaseLayoutAsync failed: {ex.Message}")
            Return False
        End Try
    End Function


    ''' <summary>
    ''' Fetches all registered base layouts for a specific server (Guild) to use in Autocomplete.
    ''' </summary>
    Public Async Function GetBaseLayoutsAsync(guildId As ULong) As Task(Of List(Of Tuple(Of Integer, String)))
        ' Returns: Item1 = layout_id, Item2 = layout_name
        Dim layoutsList As New List(Of Tuple(Of Integer, String))()
        Dim query As String = "SELECT layout_id, layout_name FROM base_layouts WHERE guild_id = :guildId ORDER BY layout_name"

        Try
            ' Uses your global connection pooling function
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("guildId", OracleDbType.Int64)).Value = Convert.ToInt64(guildId)
                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                    While Await reader.ReadAsync()
                        layoutsList.Add(Tuple.Create(reader.GetInt32(0), reader.GetString(1)))
                    End While
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetBaseLayoutsAsync failed: {ex.Message}")
        End Try
        Return layoutsList
    End Function

    ''' <summary>
    ''' Fetches full details for a single layout including the information field.
    ''' </summary>
    Public Async Function GetLayoutDetailsAsync(layoutId As Integer) As Task(Of Dictionary(Of String, String))
        Dim details As New Dictionary(Of String, String)()
        Dim query As String = "SELECT layout_name, coc_link, coc_link_2, image_link, layout_info FROM base_layouts WHERE layout_id = :layoutId"
        Try
            Using cmd As New OracleCommand(query, conn)
                cmd.Parameters.Add(New OracleParameter("layoutId", OracleDbType.Int32)).Value = layoutId
                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                    If Await reader.ReadAsync() Then
                        details("name") = reader.GetString(0)
                        details("link1") = reader.GetString(1)
                        details("link2") = If(reader.IsDBNull(2), "", reader.GetString(2))
                        details("image") = If(reader.IsDBNull(3), "", reader.GetString(3))
                        details("info") = If(reader.IsDBNull(4), "", reader.GetString(4)) ' Added field
                    End If
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetLayoutDetailsAsync failed: {ex.Message}")
        End Try
        Return details
    End Function
    ''' <summary>
    ''' Fetches all layout records from the base_layouts table.
    ''' </summary>
    Public Async Function GetAllLayoutsAsync() As Task(Of List(Of Dictionary(Of String, String)))
        Dim recordsList As New List(Of Dictionary(Of String, String))()
        Dim query As String = "SELECT layout_id, layout_name, coc_link, coc_link_2, image_link, layout_info FROM base_layouts ORDER BY layout_name ASC"

        Try
            Using cmd As New OracleCommand(query, conn)
                Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                    While Await reader.ReadAsync()
                        Dim details As New Dictionary(Of String, String)()

                        ' Safely map and read row data fields with DBNull validation
                        details("id") = reader.GetInt32(0).ToString()
                        details("name") = If(reader.IsDBNull(1), "Unnamed Layout", reader.GetString(1))
                        details("link1") = If(reader.IsDBNull(2), "", reader.GetString(2))
                        details("link2") = If(reader.IsDBNull(3), "", reader.GetString(3))
                        details("image") = If(reader.IsDBNull(4), "", reader.GetString(4))
                        details("info") = If(reader.IsDBNull(5), "", reader.GetString(5))

                        recordsList.Add(details)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetAllLayoutsAsync failed: {ex.Message}")
        End Try

        Return recordsList
    End Function


    Public Async Function GetLinkedAccountsAsync(discordId As String) As Task(Of List(Of Tuple(Of String, String, String)))
        Dim accountsList As New List(Of Tuple(Of String, String, String))()
        Dim sql As String = "SELECT coc_tag, coc_name, verified FROM discord_users WHERE id = :discordId"

        Try
            ' FIX: Create a LOCAL connection instance for this specific task thread
            Using conn As New OracleConnection(ConnectionString)

                ' Check connection state safely using the full namespace to avoid BC30451
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Using cmd As New OracleCommand(sql, conn)
                    ' Bind the parameter securely
                    cmd.Parameters.Add(New OracleParameter("discordId", OracleDbType.Varchar2)).Value = discordId

                    Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                        While Await reader.ReadAsync()
                            Dim tag As String = reader("coc_tag").ToString()
                            Dim name As String = reader("coc_name").ToString()
                            Dim verified As String = reader("verified").ToString()

                            If String.IsNullOrEmpty(verified) Then verified = "No"

                            accountsList.Add(Tuple.Create(tag, name, verified))
                        End While
                    End Using
                End Using
            End Using ' Connection is automatically closed and returned to the pool here
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetLinkedAccountsAsync failed: {ex.Message}")
            Throw
        End Try

        Return accountsList
    End Function
    Public Async Function GetAllRegisteredUsersAsync() As Task(Of List(Of Tuple(Of String, String)))
        Dim userList As New List(Of Tuple(Of String, String))()

        ' SAFEST ORACLE SQL: Ranks rows per ID. 
        ' NVL ensures that even if coc_tag is completely NULL/Empty, Oracle treats it as a valid string '#0' 
        ' so the analytical function ROW_NUMBER never crashes or drops the user!
        Dim sql As String = "SELECT id, final_name FROM (" &
                         "  SELECT id, " &
                         "         COALESCE(display_name, username) as final_name, " &
                         "         ROW_NUMBER() OVER (PARTITION BY id ORDER BY NVL(coc_tag, '#0') DESC) as rn " &
                         "  FROM discord_users" &
                         ") WHERE rn = 1"

        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Using cmd As New OracleCommand(sql, conn)
                    Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                        While Await reader.ReadAsync()
                            Dim id As String = reader("id").ToString().Trim()
                            Dim finalName As String = reader("final_name").ToString().Trim()

                            If Not String.IsNullOrEmpty(id) AndAlso Not String.IsNullOrEmpty(finalName) Then
                                userList.Add(Tuple.Create(id, finalName))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[CRITICAL DB ERROR] GetAllRegisteredUsersAsync failed: {ex.Message}")
        End Try

        Return userList
    End Function
    Public Async Function GetIdByNameAsync(searchName As String) As Task(Of String)
        Dim foundId As String = ""

        ' FIX: We use LOWER() on both the column and the parameter to make the search 100% case-insensitive!
        Dim sql As String = "SELECT id FROM discord_users WHERE LOWER(display_name) = :name OR LOWER(username) = :name FETCH FIRST 1 ROWS ONLY"

        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()
                Using cmd As New OracleCommand(sql, conn)
                    ' Bind the search parameter strictly in lowercase
                    cmd.Parameters.Add(New OracleParameter("name", OracleDbType.Varchar2)).Value = searchName.ToLower().Trim()

                    Dim result = Await cmd.ExecuteScalarAsync()
                    If result IsNot Nothing Then foundId = result.ToString()
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetIdByNameAsync failed: {ex.Message}")
        End Try
        Return foundId
    End Function
    ''' <summary>
    ''' Dynamically creates a roster table in Oracle. Drops the table first if it already exists.
    ''' </summary>
    Public Async Function CreateDynamicRosterTableAsync(tableName As String) As Task
        ' Safe Dynamic DDL compilation
        Dim dropSql As String = $"BEGIN EXECUTE IMMEDIATE 'DROP TABLE {tableName}'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;"

        Dim createSql As String = $"CREATE TABLE {tableName} (" &
                                  "  player_tag VARCHAR2(15) NOT NULL, " &
                                  "  player_name VARCHAR2(100), " &
                                  "  th_level NUMBER(2), " &
                                  "  weight NUMBER(6) DEFAULT 0, " &
                                  "  clan_name VARCHAR2(100), " &
                                  "  roster_name VARCHAR2(100), " &
                                  "  discord_id NUMBER(20), " &
                                  "  discord_name VARCHAR2(100), " &
                                  $"  CONSTRAINT pk_{tableName} PRIMARY KEY (player_tag), " &
                                  $"  CONSTRAINT chk_{tableName}_w CHECK (weight <= 180000)" &
                                  ")"
        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()

                ' Step A: Drop old table if exists (ignores ORA-00942 table-not-found error via PL/SQL block)
                Using dropCmd As New OracleCommand(dropSql, conn)
                    Await dropCmd.ExecuteNonQueryAsync()
                End Using

                ' Step B: Create fresh table
                Using createCmd As New OracleCommand(createSql, conn)
                    Await createCmd.ExecuteNonQueryAsync()
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB DYNAMIC ERROR] Failed to create table {tableName}: {ex.Message}")
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Bulk inserts the completed parallel dataset using the strongly typed RosterPlayer class.
    ''' </summary>
    Public Async Function BulkInsertDynamicRosterAsync(tableName As String, playerList As List(Of RosterPlayer)) As Task(Of Integer)
        If playerList Is Nothing OrElse playerList.Count = 0 Then Return 0

        Dim totalCount As Integer = playerList.Count

        Dim pTags(totalCount - 1) As String
        Dim pNames(totalCount - 1) As String
        Dim thLevels(totalCount - 1) As Integer
        Dim weights(totalCount - 1) As Integer
        Dim cNames(totalCount - 1) As String
        Dim rNames(totalCount - 1) As String
        Dim discordIds(totalCount - 1) As Object
        Dim discordNames(totalCount - 1) As Object

        For i As Integer = 0 To totalCount - 1
            Dim player = playerList(i)
            pTags(i) = player.PlayerTag
            pNames(i) = player.PlayerName
            thLevels(i) = player.ThLevel
            weights(i) = player.Weight
            cNames(i) = player.ClanName
            rNames(i) = player.RosterName

            ' FIX: Sauberer, benannter Zugriff ohne .Rest oder .Item8 Abstürze
            If String.IsNullOrEmpty(player.DiscordId) Then
                discordIds(i) = DBNull.Value
                discordNames(i) = DBNull.Value
            Else
                discordIds(i) = Convert.ToInt64(player.DiscordId)
                discordNames(i) = player.DiscordName
            End If
        Next

        Dim sql As String = $"INSERT INTO {tableName} (player_tag, player_name, th_level, weight, clan_name, roster_name, discord_id, discord_name) " &
                             "VALUES (:ptag, :pname, :th, :weight, :cname, :rname, :did, :dname)"

        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()

                Using cmd As New OracleCommand(sql, conn)
                    cmd.ArrayBindCount = totalCount

                    cmd.Parameters.Add(New OracleParameter("ptag", OracleDbType.Varchar2, pTags, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("pname", OracleDbType.Varchar2, pNames, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("th", OracleDbType.Int32, thLevels, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("weight", OracleDbType.Int32, weights, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("cname", OracleDbType.Varchar2, cNames, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("rname", OracleDbType.Varchar2, rNames, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("did", OracleDbType.Int64, discordIds, ParameterDirection.Input))
                    cmd.Parameters.Add(New OracleParameter("dname", OracleDbType.Varchar2, discordNames, ParameterDirection.Input))

                    Await cmd.ExecuteNonQueryAsync()
                End Using
            End Using

            Return totalCount
        Catch ex As Exception
            Console.WriteLine($"[DB DYNAMIC ERROR] BulkInsert ArrayBinding failed for {tableName}: {ex.Message}")
            Throw
        End Try
    End Function
    ''' <summary>
    ''' Fetches clans filtered strictly by clan_category (e.g., FWA).
    ''' </summary>
    Public Async Function GetClansByCategoryAsync(category As String) As Task(Of List(Of Tuple(Of String, String, String)))
        Dim clanList As New List(Of Tuple(Of String, String, String))()

        ' FIX: Changed column name from 'category' to 'CLAN_CATEGORY'
        Dim sql As String = "SELECT clan_tag, clan_name, clan_category FROM tracked_clans WHERE LOWER(clan_category) = :cat"

        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()
                Using cmd As New OracleCommand(sql, conn)
                    cmd.Parameters.Add(New OracleParameter("cat", OracleDbType.Varchar2)).Value = category.ToLower().Trim()
                    Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                        While Await reader.ReadAsync()
                            ' Make sure to use the exact column alias 'clan_category' when reading the data
                            clanList.Add(Tuple.Create(reader("clan_tag").ToString(), reader("clan_name").ToString(), reader("clan_category").ToString()))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetClansByCategoryAsync failed: {ex.Message}")
        End Try
        Return clanList
    End Function

    ''' <summary>
    ''' Loads all existing player-to-discord mappings into a RAM dictionary to prevent database heavy-looping.
    ''' </summary>
    Public Async Function GetDiscordMappingsAsync() As Task(Of Dictionary(Of String, Tuple(Of String, String)))
        Dim mappingDict As New Dictionary(Of String, Tuple(Of String, String))()
        Dim sql As String = "SELECT id, COALESCE(display_name, username) as name, coc_tag FROM discord_users WHERE coc_tag IS NOT NULL AND coc_tag <> '#0'"

        Try
            Using conn As New OracleConnection(ConnectionString)
                If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()
                Using cmd As New OracleCommand(sql, conn)
                    Using reader As OracleDataReader = CType(Await cmd.ExecuteReaderAsync(), OracleDataReader)
                        While Await reader.ReadAsync()
                            Dim tag As String = reader("coc_tag").ToString().ToUpper().Trim()
                            Dim id As String = reader("id").ToString().Trim()
                            Dim name As String = reader("name").ToString().Trim()

                            If Not mappingDict.ContainsKey(tag) Then
                                mappingDict.Add(tag, Tuple.Create(id, name))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[DB ERROR] GetDiscordMappingsAsync failed: {ex.Message}")
        End Try
        Return mappingDict
    End Function

    ''' <summary>
    ''' <summary>
    ''' Recreates the WEIGHTS table asynchronously by dropping it if it exists, then creating it fresh.
    ''' </summary>
    Public Async Function RecreateWeightsTableAsync() As Task
        Using conn As New OracleConnection(ConnectionString)
            Try
                ' Open the connection asynchronously
                Await conn.OpenAsync()
                API_COC.DebugPrint("[DB INFO] Rebuilding relational table structure for WEIGHTS asynchronously...")

                ' PL/SQL block to drop and recreate the weights table cleanly
                Dim dropAndCreateQuery As String = "
                    BEGIN
                        -- 1. Drop table WEIGHTS if it exists
                        BEGIN
                            EXECUTE IMMEDIATE 'DROP TABLE weights PURGE';
                        EXCEPTION
                            WHEN OTHERS THEN
                                IF SQLCODE != -942 THEN RAISE; END IF; -- -942 means table does not exist
                        END;

                        -- 2. Create table matching the FwaRecord properties
                        EXECUTE IMMEDIATE 'CREATE TABLE weights (
                            tag VARCHAR2(20) PRIMARY KEY,
                            weight NUMBER,
                            townhall NUMBER,
                            last_modified VARCHAR2(50),
                            fetched_at TIMESTAMP DEFAULT SYSDATE
                        )';
                    END;"

                Using cmd As New OracleCommand(dropAndCreateQuery, conn)
                    ' Execute the structural query asynchronously
                    Await cmd.ExecuteNonQueryAsync()
                    API_COC.DebugPrint("[DB SUCCESS] Table 'WEIGHTS' successfully recreated asynchronously.")
                End Using

            Catch ex As Exception
                API_COC.DebugPrint("[DB ERROR] RecreateWeightsTableAsync failed: " & ex.Message)
                Throw
            End Try
        End Using
    End Function    ''' </summary>
    Public Async Function SavePlayerWeightToDbAsync(playerTag As String, weight As Integer, thLevel As String) As Task
        If conn Is Nothing OrElse conn.State <> System.Data.ConnectionState.Open Then Return

        Dim formattedTag As String = playerTag.ToUpper().Trim()
        Dim formattedTh As String = thLevel.ToUpper().Trim()
        If Not formattedTh.StartsWith("TH") Then formattedTh = "TH" & formattedTh

        ' Sicheres SQL MERGE-Statement (Upsert) für Oracle
        Dim mergeSql As String = "
            MERGE INTO WEIGHTS t
            USING (SELECT :tag AS tag FROM dual) s
            ON (t.TAG = s.tag)
            WHEN MATCHED THEN
                UPDATE SET t.WEIGHT = :weight, t.TH = :th
            WHEN NOT MATCHED THEN
                INSERT (TAG, WEIGHT, TH) VALUES (:tag, :weight, :th)"

        Try
            Using cmd As New OracleCommand(mergeSql, conn)
                cmd.Parameters.Add(New OracleParameter("tag", formattedTag))
                cmd.Parameters.Add(New OracleParameter("weight", weight))
                cmd.Parameters.Add(New OracleParameter("th", formattedTh))
                Await cmd.ExecuteNonQueryAsync()
            End Using
        Catch ex As Exception
            API_COC.DebugPrint($"[DB ERROR] Failed to save player weight for {formattedTag}: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' Saves a list of FwaRecord structures efficiently using Oracle Array Binding (Bulk Insert) asynchronously.
    ''' </summary>
    Public Async Function SaveFwaDataAsync(records As List(Of FwaRecord)) As Task
        If records Is Nothing OrElse records.Count = 0 Then Return

        Using conn As New OracleConnection(ConnectionString)
            Try
                Await conn.OpenAsync()

                ' 1. Clear old data from the table asynchronously
                Using truncateCmd As New OracleCommand("TRUNCATE TABLE weights", conn)
                    Await truncateCmd.ExecuteNonQueryAsync()
                End Using

                ' 2. Prepare arrays for Bulk Insertion
                Dim count As Integer = records.Count
                Dim tags(count - 1) As String
                Dim weights(count - 1) As Integer
                Dim townhalls(count - 1) As Integer
                Dim lastModifieds(count - 1) As String

                For i As Integer = 0 To count - 1
                    tags(i) = records(i).Tag
                    weights(i) = records(i).Weight
                    townhalls(i) = records(i).Townhall
                    lastModifieds(i) = records(i).LastModified
                Next

                ' 3. Execute bulk insert statement
                Dim insertQuery As String = "INSERT INTO weights (tag, weight, townhall, last_modified) VALUES (:tag, :weight, :townhall, :last_modified)"

                Using insertCmd As New OracleCommand(insertQuery, conn)
                    ' Enable Oracle Array Binding
                    insertCmd.ArrayBindCount = count

                    ' Bind compiled arrays directly to parameters
                    insertCmd.Parameters.Add(New OracleParameter("tag", OracleDbType.Varchar2, tags, ParameterDirection.Input))
                    insertCmd.Parameters.Add(New OracleParameter("weight", OracleDbType.Int32, weights, ParameterDirection.Input))
                    insertCmd.Parameters.Add(New OracleParameter("townhall", OracleDbType.Int32, townhalls, ParameterDirection.Input))
                    insertCmd.Parameters.Add(New OracleParameter("last_modified", OracleDbType.Varchar2, lastModifieds, ParameterDirection.Input))

                    ' Execute bulk insert asynchronously
                    Await insertCmd.ExecuteNonQueryAsync()
                    API_COC.DebugPrint($"[DB SUCCESS] Successfully bulk-inserted {count} records into WEIGHTS asynchronously.")
                End Using

            Catch ex As Exception
                API_COC.DebugPrint("[DB ERROR] SaveFwaDataAsync bulk insert failed: " & ex.Message)
            End Try
        End Using
    End Function
    ''' <summary>
    ''' Queries a specific player/clan tag instantly and asynchronously from the relational column structure.
    ''' </summary>
    Public Async Function GetWeightByTagAsync(searchTag As String) As Task(Of FwaRecord)
        Dim normalizedTag As String = searchTag.Trim().ToUpper()
        Dim result As FwaRecord = Nothing

        Using conn As New OracleConnection(ConnectionString)
            Try
                Await conn.OpenAsync()
                Dim selectQuery As String = "SELECT tag, weight, townhall, last_modified FROM weights WHERE UPPER(tag) = :tagValue"

                Using cmd As New OracleCommand(selectQuery, conn)
                    cmd.Parameters.Add(New OracleParameter("tagValue", OracleDbType.Varchar2)).Value = normalizedTag

                    ' Use ExecuteReaderAsync for non-blocking database reads
                    Using reader As DbDataReader = Await cmd.ExecuteReaderAsync()
                        If Await reader.ReadAsync() Then
                            result = New FwaRecord() With {
                                .Tag = reader("tag").ToString(),
                                .Weight = Convert.ToInt32(reader("weight")),
                                .Townhall = Convert.ToInt32(reader("townhall")),
                                .LastModified = reader("last_modified").ToString()
                            }
                        End If
                    End Using
                End Using
            Catch ex As Exception
                API_COC.DebugPrint("[DB ERROR] GetWeightByTagAsync query failed: " & ex.Message)
            End Try
        End Using
        Return result
    End Function
    ''' <summary>
    ''' Fetches the current FWA player weights safely and maps them directly to a typed list.
    ''' </summary>
    Public Async Function GetPlayerWeightsFromWeb() As Task(Of List(Of FwaRecord))
        Const FwaWeightsUrl As String = "https://fwastats.com/Weights.json"
        Dim fallbackList As New List(Of FwaRecord)()

        Dim handler As New HttpClientHandler() With {
            .AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
        }

        Using httpClient As New HttpClient(handler)
            httpClient.DefaultRequestHeaders.Clear()
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json")
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")

            Try
                API_COC.DebugPrint("[FWA INFO] Requesting response headers from fwastats.com...")

                Using response As HttpResponseMessage = Await httpClient.GetAsync(FwaWeightsUrl, HttpCompletionOption.ResponseHeadersRead)
                    ' Ensure we got a HTTP 200 OK before processing the stream
                    response.EnsureSuccessStatusCode()

                    Using httpStream As Stream = Await response.Content.ReadAsStreamAsync()
                        Using streamReader As New StreamReader(httpStream, System.Text.Encoding.UTF8)
                            Using jsonReader As New JsonTextReader(streamReader)

                                ' FIX: Do NOT call jsonReader.Read() here! 
                                ' Let the serializer handle the stream from position 0.
                                Dim serializer As New JsonSerializer()

                                ' Direct stream deserialization without consuming tokens beforehand
                                Dim records As List(Of FwaRecord) = serializer.Deserialize(Of List(Of FwaRecord))(jsonReader)

                                If records Is Nothing Then records = fallbackList
                                API_COC.DebugPrint($"[FWA SUCCESS] Loaded {records.Count} records cleanly via safe VM-pipeline.")
                                Return records

                            End Using
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                API_COC.DebugPrint($"[CRITICAL ERROR] GetPlayerWeightsFromWeb pipeline failed: {ex.Message}")
                Return fallbackList
            End Try
        End Using
    End Function
    Public Class FwaRecord
        <JsonProperty("tag")>
        Public Property Tag As String

        <JsonProperty("weight")>
        Public Property Weight As Integer

        <JsonProperty("townhall")>
        Public Property Townhall As Integer

        <JsonProperty("lastModified")>
        Public Property LastModified As String
    End Class
    Public Class RosterPlayer
        Public Property PlayerTag As String
        Public Property PlayerName As String
        Public Property ThLevel As Integer
        Public Property Weight As Integer
        Public Property ClanName As String
        Public Property RosterName As String
        Public Property DiscordId As String
        Public Property DiscordName As String
    End Class

End Module
