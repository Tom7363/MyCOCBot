Imports Oracle.ManagedDataAccess.Client
Imports System.IO
Imports System.Threading.Tasks
Imports System.Collections.Generic

Public Module OracleDatabaseManager

    Private conn As OracleConnection = Nothing
    ''' <summary>
    ''' Reads user, password, and data source from a configuration text file and opens the connection asynchronously.
    ''' </summary>
    ''' 
    Public Async Function ConnectDBAsync() As Task(Of OracleConnection)
        If conn Is Nothing Then
            conn = Await GetCloudConnectionAsync()
            Return conn
        End If
    End Function

    Public Function IsDBConnected() As Boolean
        ' Prüfen, ob das Objekt existiert und ob der Status auf "Open" steht
        If conn IsNot Nothing AndAlso conn.State = System.Data.ConnectionState.Open Then
            Return True
        Else
            Return False
        End If
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
End Module
