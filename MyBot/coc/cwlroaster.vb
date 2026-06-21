Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports Discord
Imports Discord.Rest
Imports Discord.WebSocket
Imports HtmlAgilityPack
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client

Public Module CWLRoasterFunctions
    Public Class CWLRoaster
        Public Shared Async Function RegisterCWLCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            Dim rosterCreateCmd = New SlashCommandBuilder() With {
                       .Name = "roaster-create",
                       .Description = "Fetches FWA members via API and creates a custom roster table."
                    }

            Dim rosterGetCmd As New SlashCommandBuilder()
            rosterGetCmd.WithName("roaster-get") ' Wichtig: Nur Kleinbuchstaben, keine Sonderzeichen
            rosterGetCmd.WithDescription("Ruft die aktuelle Roster-Aufstellung aus der Oracle DB ab.")

            ' Parameter 1: dbname (Datenbank-Instanz)
            rosterGetCmd.AddOption("dbname", ApplicationCommandOptionType.String, "Name der Datenbank-Instanz", isRequired:=True)

            ' Parameter 3: cwl_name (Name des Rosters / Tabellenname)
            rosterGetCmd.AddOption("cwl_name", ApplicationCommandOptionType.String, "Name des spezifischen Roster-Profils (Tabellenname)", isRequired:=True)

            Dim cwlInfoCmd As New SlashCommandBuilder()
            cwlInfoCmd.WithName("cwl-info")
            cwlInfoCmd.WithDescription("Displays live CWL league tiers for all tracked database clans.")

            Dim cwlStatusCmd As New SlashCommandBuilder()
            cwlStatusCmd.WithName("cwl-status")
            cwlStatusCmd.WithDescription("Shows live countdowns and missing attacks for all CWL clans.")
            Await guild.CreateApplicationCommandAsync(cwlStatusCmd.Build())

            Await guild.CreateApplicationCommandAsync(rosterCreateCmd.Build())
            Await guild.CreateApplicationCommandAsync(rosterGetCmd.Build())
            Await guild.CreateApplicationCommandAsync(cwlInfoCmd.Build())



        End Function


        ''' <summary>
        ''' Fetches all members of FWA clans and dynamically creates a custom-named table.
        ''' Activated via Discord SocketSlashCommand interface interaction.
        ''' </summary>
        Public Shared Async Function HandleRosterCreateAsync(command As SocketSlashCommand) As Task
            ' 1. Acknowledge and secure the critical 3-second Discord API interaction window
            Await command.DeferAsync()

            ' 2. Safely extract the raw string parameter entered by the user
            Dim tableNameOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "table-name")
            If tableNameOption Is Nothing OrElse tableNameOption.Value Is Nothing Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ **Error:** Missing required parameter `tablename`.")
                Return
            End If

            ' 3. Sanitize input to conform to strict Oracle Database naming rules
            Dim rawTableName As String = tableNameOption.Value.ToString()
            Dim sanitizedTableName As String = rawTableName.Replace("#", "").Replace(" ", "_").Trim().ToUpper()

            If String.IsNullOrEmpty(sanitizedTableName) OrElse sanitizedTableName.Length > 30 Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ **Error:** Invalid table name. Use alphanumeric characters only (max 30).")
                Return
            End If

            ' 4. Send initial pipeline progress update tracking state
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"⏳ **Step 1/4:** Recreating dynamic table `{sanitizedTableName}` on Oracle Cloud...")

            ' Variables to handle conditional error routing outside the Catch block scope
            Dim isCrashed As Boolean = False
            Dim errorMessage As String = ""

            Try
                ' =========================================================================
                ' STEP A: Recreate the Target Table
                ' =========================================================================
                Await CreateDynamicRosterTableAsync(sanitizedTableName)
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "⏳ **Step 2/4:** Table created. Loading Discord user mapping dictionary into memory...")

                ' =========================================================================
                ' STEP B: Load Performance Mappings from Database Cache
                ' =========================================================================
                Dim discordMappings As Dictionary(Of String, Tuple(Of String, String)) = Await GetDiscordMappingsAsync()
                Dim fwaClans As List(Of Tuple(Of String, String, String)) = Await GetClansByCategoryAsync("FWA")

                If fwaClans.Count = 0 Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ **Pipeline Aborted:** No clans categorized as 'FWA' were found in `tracked_clans` table.")
                    Return
                End If

                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"⏳ **Step 3/4:** Found {fwaClans.Count} FWA clans. Fetching live player rosters via Cocapi...")
                ' =========================================================================
                ' STEP C: Query Roster Data with Clan Name & Roster Name via Clash of Clans Web API
                ' =========================================================================
                ' FIX: Nutzt jetzt die saubere Objektklasse statt des fehlerhaften Tuples
                Dim finalRosterList As New List(Of RosterPlayer)()

                Dim cocApiClient As New ClashOfClansAPI(CocService.apiToken)

                For Each clan In fwaClans
                    Dim clanTag As String = clan.Item1
                    Dim clanJson As Newtonsoft.Json.Linq.JObject = Await cocApiClient.GetClanDataAsync(clanTag)

                    If clanJson IsNot Nothing AndAlso clanJson("memberList") IsNot Nothing Then
                        Dim currentClanName As String = If(clanJson("name")?.ToString(), "Unknown Clan")
                        Dim memberListArray As Newtonsoft.Json.Linq.JArray = CType(clanJson("memberList"), Newtonsoft.Json.Linq.JArray)

                        For Each member In memberListArray
                            Dim rawPlayerTag As String = member("tag")?.ToString()
                            If String.IsNullOrEmpty(rawPlayerTag) Then Continue For

                            Dim normalizedPlayerTag As String = rawPlayerTag.ToUpper().Trim()

                            Dim weightRecord = Await GetWeightByTagAsync(normalizedPlayerTag)
                            Dim resolvedWeight As Integer = If(weightRecord IsNot Nothing, weightRecord.Weight, 0)

                            Dim discordId As String = ""
                            Dim discordName As String = ""
                            If discordMappings.ContainsKey(normalizedPlayerTag) Then
                                discordId = discordMappings(normalizedPlayerTag).Item1
                                discordName = discordMappings(normalizedPlayerTag).Item2
                            End If

                            ' Befülle das Objekt mit sprechenden Namen
                            Dim newPlayer As New RosterPlayer() With {
                            .PlayerTag = normalizedPlayerTag,
                            .PlayerName = If(member("name")?.ToString(), ""),
                            .ThLevel = If(member("townHallLevel") IsNot Nothing, Convert.ToInt32(member("townHallLevel")), 0),
                            .Weight = resolvedWeight,
                            .ClanName = currentClanName,
                            .RosterName = " ",
                            .DiscordId = discordId,
                            .DiscordName = discordName
                        }

                            finalRosterList.Add(newPlayer)
                        Next
                    End If
                Next

                If finalRosterList.Count = 0 Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ **Pipeline Aborted:** Successfully scanned clans, but retrieved zero active players.")
                    Return
                End If

                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"⏳ **Step 4/4:** Scraping finished. Bulk uploading {finalRosterList.Count} players directly to Oracle AI Database...")

                ' =========================================================================
                ' STEP D: Perform Single-Roundtrip High-Speed Bulk Insertion
                ' =========================================================================
                Dim savedRowsCount As Integer = Await BulkInsertDynamicRosterAsync(sanitizedTableName, finalRosterList)

                ' Build a professional embed response summary payload report card
                Dim successSummary As String = $"✅ **Roster Pipeline Complete!**" & vbCrLf &
                                           $"📋 Table Target: `{sanitizedTableName}`" & vbCrLf &
                                           $"🛡️ Scanned FWA Clans: `{fwaClans.Count}`" & vbCrLf &
                                           $"👥 Total Players Saved: `{savedRowsCount}`"

                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = successSummary)
                API_COC.DebugPrint($"[PIPELINE SUCCESS] Executed roster compilation into table {sanitizedTableName}.")
                Return

            Catch ex As Exception
                ' Safe execution boundary fallback layout parsing 
                API_COC.DebugPrint($"[PIPELINE CRITICAL] Failure compiling roster for {sanitizedTableName}: {ex.Message}")
                ' FIX: Extract metrics inside Catch block without performing Await operations
                isCrashed = True
                errorMessage = ex.Message
            End Try
            ' FIX: Dispatch the asynchronous Discord alert down here, safely outside of the Catch wrapper block
            If isCrashed Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ **Pipeline Execution Crashed!** " & vbCrLf & $"`{errorMessage}`")
            End If

        End Function
        Public Shared Async Function HandleRosterGetAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()
            Dim rosterTask = Task.Run(Async Function()
                                          Try
                                              ' Hier rufen wir Ihre Roster-Logik auf
                                              Await RosterCommands.HandleRosterGetCommandAsync(command, OracleDatabaseManager.OpenConnection)
                                          Catch ex As Exception
                                              ' Verhindert, dass unbehandelte Fehler im Hintergrund-Task den Bot crashen
                                              Console.WriteLine($"[Hintergrund-Fehler] /roster-get: {ex.Message}")
                                          End Try
                                      End Function)


        End Function
        Public Shared Async Function HandleCWLInfoAsync(command As SocketSlashCommand) As Task
            ' 1. Signal immediate processing receipt to Discord to clear the 3-second limit
            Await command.DeferAsync()

            ' 2. Relocate Oracle network calls safely to a worker background task
            Dim cwlInfoTask = Task.Run(Async Function()
                                           Try
                                               ' Pass the socket container and your Shared Friend database pipe
                                               Await RosterCommands.HandleCwlInfoCommandAsync(command, OracleDatabaseManager.OpenConnection)
                                           Catch ex As Exception
                                               Console.WriteLine($"[Background-Error] /cwl-info: {ex.Message}")
                                           End Try
                                       End Function)


        End Function
        Public Shared Async Function HandleCWLStatusAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            ' 1. SOFORT die Interaktion im Hauptthread aufschieben (Dauer: < 5ms)
            ' Das sichert das 3-Sekunden-Zeitfenster bei Discord und hält das Gateway frei!
            Await command.DeferAsync(ephemeral:=True)

            ' 2. Die komplette Initialisierung des 5-Minuten-Dashboards in den Hintergrund schieben
            ' Wir nutzen "Dim statusSetupTask = Task.Run(...)" um die VS-Warnung BC42358 zu umgehen
            Dim statusSetupTask = Task.Run(Async Function()
                                               Try
                                                   ' HIER rufen wir die Start-Methode sicher im Hintergrund-Thread auf
                                                   ' Hinweis: Stellen Sie sicher, dass Ihre globale Client-Variable exakt '_client' heißt
                                                   Await RosterCommands.StartCwlAutoUpdateAsync(command, client, OracleDatabaseManager.OpenConnection)
                                               Catch ex As Exception
                                                   Console.WriteLine($"[Hintergrund-Fehler] /cwl-status Setup: {ex.Message}")
                                               End Try
                                           End Function)

        End Function


        Public Shared Async Function HandleCWLInfoUpdate(component As SocketMessageComponent) As Task
            ' 1. SOFORT die Interaktion aufschieben (Sichert das 15-Minuten-Zeitfenster!)
            Await component.DeferAsync()

            ' Fire-and-Forget Task starten, um den Discord-Gateway-Thread frei zu halten
            Dim refreshTask = Task.Run(Async Function()
                                           Try
                                               ' Aufruf der Refresh-Logik aus RosterCommands
                                               Await RosterCommands.HandleRefreshButtonAsync(component, OracleDatabaseManager.OpenConnection)
                                           Catch ex As Exception
                                               Console.WriteLine($"[Refresh-Error]: {ex.Message}")
                                           End Try
                                       End Function)
        End Function
        Public Shared Async Function HandleCWLStatusUpdate(component As SocketMessageComponent) As Task

            ' 1. SOFORT die Interaktion aufschieben (Sichert das 15-Minuten-Zeitfenster!)
            Await component.DeferAsync()

            ' 2. Erst DANACH in den Hintergrund-Task ausbrechen
            Dim refreshStatusTask = Task.Run(Async Function()
                                                 Try
                                                     Await RosterCommands.HandleCwlStatusRefreshAsync(component, OracleDatabaseManager.OpenConnection)
                                                 Catch ex As Exception
                                                     Console.WriteLine($"[Refresh-Error] cwl-status: {ex.Message}")
                                                 End Try
                                             End Function)

        End Function
    End Class
    Public Class RosterCommands
        ' Statische Variablen für das automatische Update
        Private Shared _autoUpdateTimer As System.Timers.Timer
        Private Shared _targetChannelId As ULong
        Private Shared _lastMessageId As ULong
        Private Shared _clientRef As DiscordSocketClient
        Private Const UPDATE_INTERVAL_MINUTES As Integer = 5

        ''' <summary>
        ''' Executes the 'roaster-get' Slash Command using only dbname and cwl_name.
        ''' </summary>
        Public Shared Async Function HandleRosterGetCommandAsync(command As SocketSlashCommand, conn As OracleConnection) As Task
            ' 1. Extract exactly two user parameters from the options mapping payload
            Dim dbName As String = command.Data.Options(0).Value.ToString()
            Dim cwlName As String = command.Data.Options(1).Value.ToString() ' Index 1 points to cwl_name

            ' Generate the dynamic table name matching your HandleRosterCreateAsync setup
            Dim tableName As String = dbName.Replace(" ", "_")

            Dim playerRowsBuilder As New StringBuilder()
            Dim totalRecords As Integer = 0
            Dim totalWeight As Long = 0
            Dim finalEmbed As Discord.Embed = Nothing
            Dim isError As Boolean = False
            Dim errorMessage As String = String.Empty

            ' 2. Formulate the streamlined SQL statement hitting only structural variables
            Dim query As String = $"SELECT player_name, discord_id, weight FROM {tableName} WHERE roster_name = :rostername"


            Try
                ' Ensure global pool pipeline connection is active before calling reader streams
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Using cmd As New OracleCommand(query, conn)
                    cmd.Parameters.Add(New OracleParameter("rostername", cwlName))

                    Using reader As OracleDataReader = Await cmd.ExecuteReaderAsync()
                        While Await reader.ReadAsync()
                            totalRecords += 1

                            Dim playerName As String = reader("player_name").ToString()
                            Dim weight As Integer = Convert.ToInt32(reader("weight"))
                            Dim discordIdRaw As String = reader("discord_id").ToString()

                            totalWeight += weight

                            ' Convert pure 64-bit string representations into a live interactive Mention tag
                            Dim discordTag As String = "No Linked Account"
                            If Not String.IsNullOrEmpty(discordIdRaw) AndAlso IsNumeric(discordIdRaw) AndAlso discordIdRaw <> "0" Then
                                discordTag = $"<@{discordIdRaw}>"
                            End If

                            ' Construct row entry string items
                            playerRowsBuilder.AppendLine($"• **{playerName}** ({discordTag}) — ⚖️ `{weight}`")
                        End While
                    End Using
                End Using

                ' 3. Execute aggregate math operations safely
                Dim avgWeight As Double = 0
                If totalRecords > 0 Then
                    avgWeight = Math.Round(CDbl(totalWeight) / totalRecords / 1000, 3)
                End If

                ' Fallback for pristine or empty dataset scenarios
                Dim playerRows As String = playerRowsBuilder.ToString()
                If totalRecords = 0 Then
                    playerRows = "_No registered players found inside this roster file._"
                End If

                ' 4. Route active configuration variables into the EmbedEngine dictionary map
                Dim replacements As New Dictionary(Of String, String) From {
        {"{CWL_NAME}", cwlName},
        {"{DB_NAME}", dbName},
        {"{PLAYER_ROWS}", playerRows},
        {"{TOTAL_RECORDS}", totalRecords.ToString()},
        {"{AVG_WEIGHT}", avgWeight.ToString("F2")},
        {"{CLAN_BADGE_URL}", "https://flaticon.com"} ' Zuverlässiges MINT/Schild-Icon
    }
                finalEmbed = EmbedEngine.Render("roster_template.json", replacements)

            Catch ex As Exception
                ' Isolate exceptions safely away from direct inside-catch await actions
                isError = True
                errorMessage = ex.Message
            End Try

            ' 5. Dispatch resulting data stream safely out of the try-catch state scope
            If isError Then
                Dim errorReplacements As New Dictionary(Of String, String) From {
                {"{CWL_NAME}", "CRITICAL ERROR"},
                {"{DB_NAME}", dbName},
                {"{PLAYER_ROWS}", $"❌ **Failed to extract records from dynamic layout '{tableName}':**\n`{errorMessage}`"},
                {"{TOTAL_RECORDS}", "0"},
                {"{AVG_WEIGHT}", "0.00"},
                {"{CLAN_BADGE_URL}", "https://flaticon.com"}
            }
                Dim errorEmbed = EmbedEngine.Render("roster_template.json", errorReplacements)
                Await command.FollowupAsync(embed:=errorEmbed)
            Else
                If finalEmbed IsNot Nothing Then
                    Await command.FollowupAsync(embed:=finalEmbed)
                End If
            End If
        End Function

        ''' <summary>
        ''' Executes the 'cwl-info' Slash Command. Extracts database clusters and pulls live stats.
        ''' For unranked entries, it builds inline rosters dynamically based on custom layouts.
        ''' </summary>
        Public Shared Async Function HandleCwlInfoCommandAsync(command As SocketSlashCommand, conn As OracleConnection) As Task
            Dim clanRowsBuilder As New StringBuilder()
            Dim totalClans As Integer = 0
            Dim finalEmbed As Discord.Embed = Nothing
            Dim isError As Boolean = False
            Dim errorMessage As String = String.Empty




            ' 1. Select tracked clans where CLAN_CATEGORY is CWL
            Dim query As String = "SELECT clan_tag, clan_name FROM Tracked_clans WHERE CLAN_CATEGORY = 'CWL'"

            Try
                ' Ensure global pool pipeline connection is active
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                ' Instantiating your ClashOfClansAPI using your dynamic API token system
                Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
                Dim clanBadgeUrl As String = "https://discordapp.com" ' Discord-Standard als Fallback
                Dim isBadgeSet As Boolean = False

                Using cmd As New OracleCommand(query, conn)
                    Using reader As OracleDataReader = Await cmd.ExecuteReaderAsync()
                        While Await reader.ReadAsync()
                            totalClans += 1
                            Dim dbClanTag As String = reader("clan_tag").ToString()
                            Dim dbClanName As String = reader("clan_name").ToString()

                            Dim leagueName As String = "Unknown League"
                            Dim detailsInfo As New StringBuilder()

                            Try
                                ' 2. Request live clan data from Supercell API
                                Dim apiResponse As JObject = Await cocApi.GetClanDataAsync(dbClanTag)

                                If apiResponse IsNot Nothing Then
                                    If Not isBadgeSet AndAlso apiResponse("badgeUrls") IsNot Nothing Then
                                        clanBadgeUrl = apiResponse("badgeUrls")("medium").ToString()
                                        isBadgeSet = True ' Verhindert, dass nachfolgende Clans das Wappen überschreiben
                                    End If

                                    ' Extract league name node safely
                                    If apiResponse("warLeague") IsNot Nothing Then
                                        leagueName = apiResponse("warLeague")("name").ToString()
                                    End If

                                    ' 3. EVALUATION BRANCHING
                                    If Not String.IsNullOrEmpty(leagueName) AndAlso Not leagueName.Equals("Unranked", StringComparison.OrdinalIgnoreCase) Then
                                        ' EXTENSION 1: If Ranked -> Compute 50 - Current Members
                                        If apiResponse("members") IsNot Nothing Then
                                            Dim currentMembers As Integer = Convert.ToInt32(apiResponse("members"))
                                            Dim freeSlots As Integer = 50 - currentMembers
                                            detailsInfo.Append($" (Free Slots `{freeSlots}`)")
                                        End If
                                    Else
                                        ' EXTENSION 2: If Unranked -> Inject Roster List inline
                                        ' Parameters assigned based on request parameters:
                                        ' dbname parameter = "june 2"
                                        ' cwlname parameter = current clan_name
                                        Dim inlineRoster As String = Await GetInlineRosterDataAsync(conn, "june 2", dbClanName, apiResponse)
                                        detailsInfo.AppendLine() ' Add spacing before shifting to roster lines
                                        detailsInfo.Append(inlineRoster)
                                    End If
                                End If
                            Catch apiEx As Exception
                                leagueName = "⚠️ API Error"
                                detailsInfo.Append($" (`{apiEx.Message}`)")
                            End Try

                            ' Assemble master summary collection blocks
                            clanRowsBuilder.AppendLine($"• **{dbClanName}** (`{dbClanTag}`) — 🏆 _{leagueName}_{detailsInfo.ToString()}")
                        End While
                    End Using
                End Using

                ' Fallback for empty sets
                Dim clanRows As String = clanRowsBuilder.ToString()
                If totalClans = 0 Then
                    clanRows = "_No tracked CWL clans found inside the database pool registry._"
                End If

                Dim replacements As New Dictionary(Of String, String) From {
        {"{CLAN_ROWS}", clanRows},
        {"{TOTAL_CLANS}", totalClans.ToString()}
    }

                finalEmbed = EmbedEngine.Render("cwl_info_template.json", replacements)

            Catch ex As Exception
                isError = True
                errorMessage = ex.Message
            End Try



            Dim compBuilder As New ComponentBuilder()
            compBuilder.WithButton("🔄 Refresh Data", "refresh_cwl_info", ButtonStyle.Primary)

            ' 5. Dispatch resulting data stream safely
            If isError Then
                Dim errorReplacements As New Dictionary(Of String, String) From {
                    {"{CLAN_ROWS}", $"❌ **Failed to retrieve tracked clans from database:**\n`{errorMessage}`"},
                    {"{TOTAL_CLANS}", "0"}
                }
                Dim errorEmbed = EmbedEngine.Render("cwl_info_template.json", errorReplacements)
                Await command.FollowupAsync(embed:=errorEmbed)
            Else
                If finalEmbed IsNot Nothing Then
                    Await command.FollowupAsync(embed:=finalEmbed, components:=compBuilder.Build())

                End If
            End If
        End Function
        ''' <summary>
        ''' Helper method to fetch roster datasets for unranked clans with live status telemetry and foreign clan-member tracking.
        ''' </summary>
        Private Shared Async Function GetInlineRosterDataAsync(conn As OracleConnection, dbParam As String, cwlNameParam As String, apiResponse As JObject) As Task(Of String)
            ' 1. Derive precise lowercase table identifier name by stripping spaces
            Dim tableName As String = dbParam.Replace(" ", "").ToLower()

            Dim subBuilder As New StringBuilder()
            Dim recordCounter As Integer = 0

            ' Initialize specific analytical logging counters
            Dim inClanCount As Integer = 0
            Dim notInClanCount As Integer = 0
            Dim notInRosterCount As Integer = 0

            ' Instantiate localized structural lookups for safe O(1) RAM evaluation
            Dim liveClanMembers As New List(Of JToken)()
            Dim rosterPlayerTags As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            ' Safely populate live endpoint memory structures
            If apiResponse IsNot Nothing AndAlso apiResponse("memberList") IsNot Nothing Then
                For Each member In apiResponse("memberList")
                    If member("tag") IsNot Nothing Then
                        liveClanMembers.Add(member)
                    End If
                Next
            End If

            ' Build high-speed indexing filter array for direct verification operations
            Dim liveClanMemberTags As New HashSet(Of String)(liveClanMembers.Select(Function(m) m("tag").ToString()), StringComparer.OrdinalIgnoreCase)

            ' 2. Execute database lookup stream targeting the defined structural layout
            Dim subQuery As String = $"SELECT player_tag, player_name, discord_id, weight FROM {tableName} WHERE roster_name = :rostername"

            Try
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Using subCmd As New OracleCommand(subQuery, conn)
                    subCmd.Parameters.Add(New OracleParameter("rostername", cwlNameParam))

                    Using subReader As OracleDataReader = Await subCmd.ExecuteReaderAsync()
                        While Await subReader.ReadAsync()
                            recordCounter += 1
                            Dim pTag As String = subReader("player_tag").ToString()
                            Dim pName As String = subReader("player_name").ToString()
                            Dim pWeight As Integer = Convert.ToInt32(subReader("weight"))
                            Dim discIdRaw As String = subReader("discord_id").ToString()

                            ' Log this tag to ensure we can identify foreign actors later
                            rosterPlayerTags.Add(pTag)

                            ' Formulate safe Discord snowflakes
                            Dim dTag As String = "No Linked Account"
                            If Not String.IsNullOrEmpty(discIdRaw) AndAlso IsNumeric(discIdRaw) AndAlso discIdRaw <> "0" Then
                                dTag = $"<@{discIdRaw}>"
                            End If

                            ' Evaluate deployment conditions (Is the scheduled account physically in-game?)
                            Dim statusIcon As String = "🔴"
                            If liveClanMemberTags.Contains(pTag) Then
                                statusIcon = "🟢"
                                inClanCount += 1
                            Else
                                notInClanCount += 1
                            End If

                            subBuilder.AppendLine($"  ↳ {statusIcon} **{pName}** ({dTag}) — ⚖️ `{pWeight}`")
                        End While
                    End Using
                End Using

                ' 3. COMPUTE ANOMALIES: Locate actors physically inside the clan but absent from the database planning setup
                Dim extraPlayersBuilder As New StringBuilder()
                For Each member In liveClanMembers
                    Dim mTag As String = member("tag").ToString()

                    ' If the current active clan account tag was never processed during our database reader loop:
                    If Not rosterPlayerTags.Contains(mTag) Then
                        notInRosterCount += 1
                        Dim mName As String = member("name").ToString()
                        Dim mTownhall As String = If(member("townHallLevel") IsNot Nothing, $"TH{member("townHallLevel")}", "TH?")

                        ' Print foreign accounts with a blue indicator, showing their live Town Hall level instead of a database weight
                        extraPlayersBuilder.AppendLine($"  ↳ 🔵 **{mName}** (`{mTag}`) — 🏠 `{mTownhall}` _[Not in Roster]_")
                    End If
                Next

                ' Append foreign actors neatly sectioned off if any are detected
                If notInRosterCount > 0 Then
                    subBuilder.AppendLine("  ↳ ─── *Extra Clan Members (Not in Roster):* ───")
                    subBuilder.Append(extraPlayersBuilder.ToString())
                End If

                ' Total fallback configuration check
                If recordCounter = 0 AndAlso notInRosterCount = 0 Then
                    Return $"  ↳ _(No roster data found and no physical players found inside the clan endpoint)_"
                End If

                ' 4. Append the comprehensive summary statistics row at the foot of the layout tree
                subBuilder.AppendLine($"  ↳ 📊 **Roster Status:** 🟢 In Clan: `{inClanCount}` | 🔴 Missing: `{notInClanCount}` | 🔵 Extra: `{notInRosterCount}`")

                Return subBuilder.ToString()

            Catch ex As Exception
                Return $"  ↳ ⚠️ _(Failed to parse layout registry '{tableName}': {ex.Message})_"
            End Try
        End Function

        Public Shared Async Function HandleRefreshButtonAsync(component As SocketMessageComponent, conn As OracleConnection) As Task
            ' 1. REMOVE the component.DeferAsync() line from here completely!
            ' Using component.UpdateAsync() down below handles the state transition natively.

            Dim clanRowsBuilder As New StringBuilder()
            Dim totalClans As Integer = 0
            Dim finalEmbed As Discord.Embed = Nothing
            Dim isError As Boolean = False
            Dim errorMessage As String = String.Empty

            ' Keep track of the first clan's badge layout
            Dim clanBadgeUrl As String = "https://discordapp.com"
            Dim isBadgeSet As Boolean = False

            Dim query As String = "SELECT clan_tag, clan_name FROM Tracked_clans WHERE CLAN_CATEGORY = 'CWL'"

            Try
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

                Using cmd As New OracleCommand(query, conn)
                    Using reader As OracleDataReader = Await cmd.ExecuteReaderAsync()
                        While Await reader.ReadAsync()
                            totalClans += 1
                            Dim dbClanTag As String = reader("clan_tag").ToString()
                            Dim dbClanName As String = reader("clan_name").ToString()
                            Dim leagueName As String = "Unknown League"
                            Dim detailsInfo As New StringBuilder()

                            Try
                                Dim apiResponse As JObject = Await cocApi.GetClanDataAsync(dbClanTag)
                                If apiResponse IsNot Nothing Then
                                    ' Keep the live badge asset up to date
                                    If Not isBadgeSet AndAlso apiResponse("badgeUrls") IsNot Nothing Then
                                        clanBadgeUrl = apiResponse("badgeUrls")("medium").ToString()
                                        isBadgeSet = True
                                    End If

                                    If apiResponse("warLeague") IsNot Nothing Then
                                        leagueName = apiResponse("warLeague")("name").ToString()
                                    End If

                                    If Not String.IsNullOrEmpty(leagueName) AndAlso Not leagueName.Equals("Unranked", StringComparison.OrdinalIgnoreCase) Then
                                        If apiResponse("members") IsNot Nothing Then
                                            Dim currentMembers As Integer = Convert.ToInt32(apiResponse("members"))
                                            Dim freeSlots As Integer = 50 - currentMembers
                                            detailsInfo.Append($" (Free{ChrW(&HA0)}Slots{ChrW(&HA0)}{freeSlots})")
                                        End If
                                    Else
                                        ' Pull live comparative metrics for unranked rosters
                                        Dim inlineRoster As String = Await GetInlineRosterDataAsync(conn, "june 2", dbClanName, apiResponse)
                                        detailsInfo.AppendLine()
                                        detailsInfo.Append(inlineRoster)
                                    End If
                                End If
                            Catch apiEx As Exception
                                leagueName = "⚠️ API Error"
                            End Try

                            clanRowsBuilder.AppendLine($"• **{dbClanName}** (`{dbClanTag}`) — 🏆 _{leagueName}_{detailsInfo.ToString()}")
                        End While
                    End Using
                End Using

                Dim clanRows As String = clanRowsBuilder.ToString()
                If totalClans = 0 Then clanRows = "_No tracked CWL clans found inside the database pool registry._"

                Dim replacements As New Dictionary(Of String, String) From {
                {"{CLAN_ROWS}", clanRows},
                {"{TOTAL_CLANS}", totalClans.ToString()}
            }
                finalEmbed = EmbedEngine.Render("cwl_info_template.json", replacements)

            Catch ex As Exception
                isError = True
                errorMessage = ex.Message
            End Try

            ' =========================================================================
            ' NATIVE FIX: Use UpdateAsync to modify button-instigated views
            ' =========================================================================
            If isError Then
                Dim errorReplacements As New Dictionary(Of String, String) From {
                {"{CLAN_ROWS}", $"❌ **Refresh failed:**\n`{errorMessage}`"},
                {"{TOTAL_CLANS}", "0"}
            }
                Dim errorEmbed = EmbedEngine.Render("cwl_info_template.json", errorReplacements)

                ' Update layout context via structural inline lambda mapping
                Await component.UpdateAsync(Sub(msg) msg.Embed = errorEmbed)
            Else
                If finalEmbed IsNot Nothing Then
                    ' This seamlessly modifies the active layout in the chat client
                    Await component.UpdateAsync(Sub(msg) msg.Embed = finalEmbed)
                End If
            End If
        End Function

        Public Shared Async Function HandleCwlStatusCommandAsync(command As SocketSlashCommand, conn As OracleConnection) As Task
            Dim builder As EmbedBuilder = Await ProcessCwlStatusDataAsync(conn)

            Dim compBuilder As New ComponentBuilder()
            compBuilder.WithButton("🔄 Refresh Status", "refresh_cwl_status", ButtonStyle.Danger)

            Await command.FollowupAsync(embed:=builder.Build(), components:=compBuilder.Build())
        End Function

        ''' <summary>
        ''' Core data processor shared between the initial command execution and the button refresh event.
        Private Shared Async Function ProcessCwlStatusDataAsync(conn As OracleConnection) As Task(Of EmbedBuilder)
            Dim currentUtcTime As DateTime = DateTime.UtcNow
            Dim unixTimestamp As Long = CLng((currentUtcTime - New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds)
            Dim discordLiveTime As String = $"<t:{unixTimestamp}:T> (<t:{unixTimestamp}:R>)"

            Dim timeReplacements As New Dictionary(Of String, String) From {{"{UPDATED_TIME}", discordLiveTime}}

            Dim baseEmbed As Embed = EmbedEngine.Render("cwl_status_template.json", timeReplacements)
            Dim builder As EmbedBuilder = baseEmbed.ToEmbedBuilder()

            Dim totalClans As Integer = 0
            Dim query As String = "SELECT clan_tag, clan_name FROM Tracked_clans WHERE CLAN_CATEGORY = 'CWL'"


            Try
                ' 1. Oracle-Verbindung sicherstellen
                If conn.State <> System.Data.ConnectionState.Open Then
                    Await conn.OpenAsync()
                End If

                Using client As New HttpClient()
                    ' Token-Zuweisung über Ihre korrekte Variable CocService.apiToken
                    client.DefaultRequestHeaders.Authorization = New System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CocService.apiToken)

                    Using cmd As New OracleCommand(query, conn)
                        Using reader As OracleDataReader = Await cmd.ExecuteReaderAsync()
                            While Await reader.ReadAsync()
                                totalClans += 1
                                Dim dbClanTag As String = reader("clan_tag").ToString()
                                Dim dbClanName As String = reader("clan_name").ToString()
                                Dim cleanTag As String = dbClanTag.Replace("#", "")
                                Dim clanContentBuilder As New StringBuilder()




                                Dim cleanTagForLink As String = dbClanTag.Replace("#", "%23")
                                Dim clashLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTagForLink}"
                                Dim fieldTitle As String = $"🏆 {dbClanName} ({dbClanTag})"

                                Try
                                    ' =========================================================================
                                    ' STEP 1: LEAGUEGROUP ABRUFEN & WARTAG EXTRAHIEREN
                                    ' =========================================================================
                                    Dim lgUrl As String = "https://api.clashofclans.com/v1/clans/%23" & cleanTag & "/currentwar/leaguegroup"

                                    Dim lgResponse As HttpResponseMessage = Await client.GetAsync(lgUrl)

                                    Dim currentRound As Integer = 0
                                    Dim currentRound2 As Integer = 0
                                    Dim totalRounds As Integer = 0
                                    Dim isCwlRegistered As Boolean = False
                                    Dim activeWarTag As String = String.Empty
                                    Dim activeWarTag2 As String = String.Empty
                                    Dim warJson As JObject = Nothing
                                    Dim PREPJson As JObject = Nothing
                                    Dim warUrl As String = String.Empty

                                    If lgResponse.IsSuccessStatusCode Then
                                        isCwlRegistered = True
                                        Dim lgJson As JObject = JObject.Parse(Await lgResponse.Content.ReadAsStringAsync())

                                        If lgJson("rounds") IsNot Nothing Then
                                            Dim roundsArray As JArray = CType(lgJson("rounds"), JArray)
                                            totalRounds = roundsArray.Count
                                            'Console.WriteLine($"DEBUG: Found {totalRounds} rounds in league group for clan {dbClanName}.") ' Debug-Ausgabe
                                            ' Durch die Runden iterieren, um die aktive Runde und deren ersten WarTag zu finden
                                            For i As Integer = roundsArray.Count - 1 To 0 Step -1
                                                Dim roundObj = roundsArray(i)
                                                If roundObj("warTags") IsNot Nothing AndAlso roundObj("warTags").HasValues Then

                                                    Dim warTagsArray As JArray = CType(roundObj("warTags"), JArray)
                                                    For Each wartagrecord In warTagsArray

                                                        Dim wt = wartagrecord.ToString().Replace("#", "")

                                                        If wt <> "0" Then
                                                            ' Versuche zuerst den spezifischen CWL-Kriegs-Tag aus der Gruppe
                                                            If Not String.IsNullOrEmpty(wt) Then

                                                                warUrl = "https://api.clashofclans.com/v1/clanwarleagues/wars/%23" & wt
                                                                'Console.WriteLine("U" & warUrl)

                                                                Dim warHttpResponse As HttpResponseMessage = Await client.GetAsync(warUrl)
                                                                'Console.WriteLine("R1" & warHttpResponse.StatusCode)

                                                                If warHttpResponse.IsSuccessStatusCode Then
                                                                    Dim tempJson As JObject = JObject.Parse(Await warHttpResponse.Content.ReadAsStringAsync())

                                                                    ' Validierung: Gehört dieser Krieg zu unserem Clan? (Heim- oder Gegner-Match)
                                                                    If tempJson IsNot Nothing AndAlso tempJson("clan") IsNot Nothing AndAlso tempJson("opponent") IsNot Nothing Then
                                                                        Dim homeTag As String = tempJson("clan")("tag").ToString()
                                                                        Dim oppTag As String = tempJson("opponent")("tag").ToString()
                                                                        Dim warState As String = tempJson("state").ToString().ToLower()

                                                                        If homeTag.Equals(dbClanTag, StringComparison.OrdinalIgnoreCase) OrElse oppTag.Equals(dbClanTag, StringComparison.OrdinalIgnoreCase) Then

                                                                            Select Case warState
                                                                                Case "preparation"
                                                                                    currentRound2 = i + 1
                                                                                    PREPJson = tempJson
                                                                                    activeWarTag2 = wt
                                                                                'Console.WriteLine("Prep" & activeWarTag2 & "R" & currentRound2)

                                                                                Case "inwar"
                                                                                    currentRound = i + 1
                                                                                    warJson = tempJson
                                                                                    activeWarTag = wt
                                                                                'Console.WriteLine("War" & activeWarTag & "R" & currentRound)
                                                                                Case "warended"
                                                                                    i = 0
                                                                            End Select
                                                                        End If
                                                                    End If
                                                                End If
                                                            End If
                                                        End If
                                                    Next
                                                End If

                                            Next
                                        Else
                                            'Console.WriteLine($"DEBUG: Found 0 rounds in league group for clan {dbClanName}.") ' Debug-Ausgabe
                                        End If
                                    End If


                                    ' =========================================================================
                                    ' STEP 3: STATI UND COUNTDOWNS AUSWERTEN
                                    ' =========================================================================
                                    Dim statusEvaluated As Boolean = False
                                    If warJson Is Nothing AndAlso PREPJson IsNot Nothing Then
                                        warJson = PREPJson
                                    End If

                                    If warJson IsNot Nothing AndAlso warJson("state") IsNot Nothing Then
                                        Dim warState As String = warJson("state").ToString().ToLower()
                                        Dim startTimeRaw As String = If(warJson("startTime") IsNot Nothing, warJson("startTime").ToString(), "")
                                        Dim endTimeRaw As String = If(warJson("endTime") IsNot Nothing, warJson("endTime").ToString(), "")
                                        Dim timeRemaining As String = "Unknown"

                                        Select Case warState
                                            Case "preparation"
                                                If Not String.IsNullOrEmpty(startTimeRaw) Then
                                                    Dim startTime As DateTime = DateTime.ParseExact(startTimeRaw.Substring(0, 15), "yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal)
                                                    Dim diff As TimeSpan = startTime - DateTime.UtcNow
                                                    timeRemaining = If(diff.Ticks > 0, $"{diff.Hours}h{ChrW(&HA0)}{diff.Minutes}m", "Starting soon")
                                                End If
                                                clanContentBuilder.AppendLine($"🔗 **In-Game Link:** [{dbClanTag}]({clashLink})")
                                                clanContentBuilder.AppendLine($"⏳ **Prep Day (Round {currentRound2})** | Starts in: `{timeRemaining}`")
                                                statusEvaluated = True
                                            Case "inwar"
                                                If Not String.IsNullOrEmpty(endTimeRaw) Then
                                                    Dim endTime As DateTime = DateTime.ParseExact(endTimeRaw.Substring(0, 15), "yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal)
                                                    Dim diff As TimeSpan = endTime - DateTime.UtcNow
                                                    timeRemaining = If(diff.Ticks > 0, $"{diff.Hours}h{ChrW(&HA0)}{diff.Minutes}m", "Ending soon")
                                                End If
                                                ' Der Link wird hier als reiner, klickbarer Tag dargestellt
                                                clanContentBuilder.AppendLine($"🔗 **In-Game Link:** [{dbClanTag}]({clashLink})")
                                                clanContentBuilder.AppendLine($"⚔️ **War Day (Round {currentRound})** | Time left: `{timeRemaining}`")

                                                Dim openCount As Integer = 0

                                                ' 1. Dynamische Erkennung des eigenen Clans im JSON
                                                Dim ourClanNode As JToken = Nothing
                                                If warJson("clan") IsNot Nothing AndAlso warJson("clan")("tag") IsNot Nothing Then
                                                    Dim homeTag As String = warJson("clan")("tag").ToString()
                                                    If homeTag.Equals(dbClanTag, StringComparison.OrdinalIgnoreCase) Then
                                                        ourClanNode = warJson("clan")
                                                    Else
                                                        ourClanNode = warJson("opponent")
                                                    End If
                                                End If

                                                ' 2. Wenn der Clan gefunden wurde, werten wir die offenen Angreifer aus
                                                If ourClanNode IsNot Nothing AndAlso ourClanNode("members") IsNot Nothing Then

                                                    ' Tabellenname für die Roster-Datenbank-Prüfung auflösen ("june 2" -> "june2")
                                                    Dim rosterTableName As String = "june2"

                                                    For Each member In ourClanNode("members")
                                                        Dim isAttackOpen As Boolean = False

                                                        If member("attacks") IsNot Nothing Then
                                                            Dim attacksArray As JArray = CType(member("attacks"), JArray)
                                                            If attacksArray.Count = 0 Then isAttackOpen = True
                                                        Else
                                                            isAttackOpen = True
                                                        End If

                                                        ' 3. Spieler hat nicht angegriffen -> Daten aus der DB ziehen und Zeile bauen
                                                        If isAttackOpen Then
                                                            openCount += 1
                                                            Dim apiPlayerTag As String = member("tag").ToString()
                                                            Dim apiPlayerName As String = member("name").ToString()

                                                            Dim discordTag As String = " -"

                                                            ' Punktgenaue asynchrone Abfrage der Discord-ID für diesen spezifischen Spieler-Tag
                                                            Dim dbQuery As String = "SELECT id FROM discord_users WHERE coc_tag = :ptag"

                                                            Try
                                                                Using dbCmd As New OracleCommand(dbQuery, conn)
                                                                    dbCmd.Parameters.Add(New OracleParameter("ptag", apiPlayerTag))
                                                                    Using dbReader As OracleDataReader = Await dbCmd.ExecuteReaderAsync()
                                                                        If Await dbReader.ReadAsync() Then
                                                                            Dim discIdRaw As String = dbReader("id").ToString()
                                                                            'Dim discIdRaw As String = dbReader("discord_id").ToString()
                                                                            If Not String.IsNullOrEmpty(discIdRaw) AndAlso IsNumeric(discIdRaw) AndAlso discIdRaw <> "0" Then
                                                                                discordTag = $"<@{discIdRaw}>"
                                                                            End If
                                                                        End If
                                                                    End Using
                                                                End Using
                                                            Catch exDb As Exception
                                                                ' Fallback bei DB-Lesefehlern (z. B. wenn der Spieler nicht im Roster der Tabelle steht)
                                                                discordTag = " --"
                                                            End Try

                                                            If clanContentBuilder.Length > 950 Then
                                                                builder.AddField($"🏆 {dbClanName} ({dbClanTag}) (Part 1)", clanContentBuilder.ToString(), False)
                                                                clanContentBuilder.Clear() ' Leert den Builder für Part 2
                                                                clanContentBuilder.AppendLine($"⚔️ **War Day (Round {currentRound})** | *Continued...*")
                                                            End If
                                                            clanContentBuilder.AppendLine($"  ↳ 🛑 **{apiPlayerName}** ({discordTag})")
                                                        End If
                                                    Next

                                                    ' Zusammenfassender Status-Indikator
                                                    If openCount = 0 Then
                                                        clanContentBuilder.AppendLine("    ↳ 🟢 **All attacks completed!**")
                                                    Else
                                                        clanContentBuilder.AppendLine($"    ↳ 📊 **Total Missing Attacks:** `{openCount}`")
                                                    End If
                                                End If
                                                statusEvaluated = True
                                            Case "warended"
                                                clanContentBuilder.AppendLine($"  ↳ 🏁 **Round {currentRound} Ended**")
                                                statusEvaluated = True
                                        End Select
                                    End If

                                    ' =========================================================================
                                    ' STEP 4: AUSFALLSCHUTZ FÜR SPEZIELLE STRUKTUREN (Z.B. TAG 1 PREP)
                                    ' =========================================================================
                                    If Not statusEvaluated Then
                                        If isCwlRegistered AndAlso currentRound > 0 Then
                                            clanContentBuilder.AppendLine($"  ↳ ⏳ **Prep Day (Round {currentRound}/{totalRounds})** | Status: `Spinning / Filling Bases`")
                                        ElseIf isCwlRegistered Then
                                            clanContentBuilder.AppendLine("  ↳ 💤 **CWL Week : not in CWL yet.**")
                                        Else
                                            clanContentBuilder.AppendLine("  ↳ 💤 **Unranked**")
                                        End If
                                    End If

                                Catch ex As Exception
                                    clanContentBuilder.AppendLine($"  ↳ ⚠️ *API Data Error: {ex.Message}*")
                                End Try

                                Dim finalFieldContent As String = clanContentBuilder.ToString()
                                Dim currentTitle As String = fieldTitle



                                ' Falls gesplittet wurde, hängen wir Part 1 oder Part 2 an den reinen Text-Titel an
                                If builder.Fields.Count > 0 AndAlso builder.Fields(builder.Fields.Count - 1).Name.Contains("(Part 1)") Then
                                    currentTitle &= " (Part 2)"
                                ElseIf clanContentBuilder.Length > 950 Then
                                    ' Falls das Feld oben in der Schleife bereits getrennt wurde
                                    currentTitle &= " (Part 1)"
                                End If

                                If finalFieldContent.Length > 1024 Then
                                    finalFieldContent = finalFieldContent.Substring(0, 1020) & "..."
                                End If

                                ' Feld ohne API-Fehler hinzufügen
                                builder.AddField(currentTitle, finalFieldContent, False)




                            End While ' End While: reader.ReadAsync
                        End Using ' End Using: reader
                    End Using ' End Using: cmd
                End Using ' End Using: client

                If totalClans = 0 Then
                    builder.WithDescription("_No tracked CWL clans found inside the database pool registry._")
                End If
                Return builder

            Catch ex As Exception
                If totalClans = 0 Then
                    builder.WithDescription("_No tracked CWL clans found inside the database pool registry._")
                End If
                Return builder
            End Try
        End Function

        Public Shared Async Function HandleCwlStatusRefreshAsync(component As SocketMessageComponent, conn As OracleConnection) As Task
            Dim builder As EmbedBuilder = Await ProcessCwlStatusDataAsync(conn)
            Await component.ModifyOriginalResponseAsync(Sub(msg) msg.Embed = builder.Build())
        End Function
        ''' <summary>
        ''' Startet das automatische Update-Dashboard basierend auf der Intervall-Konstante.
        ''' </summary>
        Public Shared Async Function StartCwlAutoUpdateAsync(command As SocketSlashCommand, client As DiscordSocketClient, conn As OracleConnection) As Task
            ' Kanal-ID speichern, in dem der Befehl ausgeführt wurde
            _targetChannelId = command.ChannelId
            _clientRef = client

            ' =========================================================================
            ' KORRIGIERT: Holt direkt den fertigen EmbedBuilder (Fehler BC30311 behoben!)
            ' =========================================================================
            Dim builder As EmbedBuilder = Await ProcessCwlStatusDataAsync(conn)

            ' Das finale Embed mit .Build() erzeugen
            Dim initialEmbed As Embed = builder.Build()

            ' Refresh-Button für manuelle Updates hinzufügen
            Dim compBuilder As New ComponentBuilder()
            compBuilder.WithButton("🔄 Refresh Status", "refresh_cwl_status", ButtonStyle.Danger)

            ' 2. Erste Nachricht fest in den Kanal posten
            Dim targetChannel = CType(Await client.GetChannelAsync(_targetChannelId), IMessageChannel)
            Dim postedMessage = Await targetChannel.SendMessageAsync(embed:=initialEmbed, components:=compBuilder.Build())
            _lastMessageId = postedMessage.Id

            ' 3. Timer einrichten (Intervall wird dynamisch aus der Minuten-Konstante berechnet)
            If _autoUpdateTimer IsNot Nothing Then
                _autoUpdateTimer.Stop()
                _autoUpdateTimer.Dispose()
            End If

            Dim intervalMilliseconds As Double = UPDATE_INTERVAL_MINUTES * 60 * 1000
            _autoUpdateTimer = New System.Timers.Timer(intervalMilliseconds)

            AddHandler _autoUpdateTimer.Elapsed, AddressOf OnAutoUpdateTimerElapsed
            _autoUpdateTimer.AutoReset = True
            _autoUpdateTimer.Start()

            ' Unsichtbare Bestätigung an den Admin senden
            Await command.FollowupAsync($"✅ Live CWL Dashboard created! This message will now auto-update every {UPDATE_INTERVAL_MINUTES} minutes.", ephemeral:=True)
        End Function

        Private Shared Sub OnAutoUpdateTimerElapsed(sender As Object, e As System.Timers.ElapsedEventArgs)
            Task.Run(Async Function()
                         Try
                             Dim conn As OracleConnection = OracleDatabaseManager.OpenConnection
                             If conn.State <> System.Data.ConnectionState.Open Then Await conn.OpenAsync()

                             ' Frischen Builder holen und im Kanal überschreiben oder neu posten
                             Dim builder As EmbedBuilder = Await ProcessCwlStatusDataAsync(conn)
                             Dim freshEmbed As Embed = builder.Build()

                             Dim compBuilder As New ComponentBuilder()
                             compBuilder.WithButton("🔄 Refresh Status", "refresh_cwl_status", ButtonStyle.Danger)

                             Dim channel = CType(Await _clientRef.GetChannelAsync(_targetChannelId), IMessageChannel)
                             If channel IsNot Nothing Then
                                 Dim messages = Await channel.GetMessagesAsync(5).FlattenAsync()
                                 Dim postCountBetween As Integer = 0
                                 Dim shouldRecreate As Boolean = False

                                 For Each msg In messages
                                     If msg.Id = _lastMessageId Then Exit For
                                     postCountBetween += 1
                                 Next

                                 If postCountBetween > 3 OrElse Not messages.Any(Function(m) m.Id = _lastMessageId) Then shouldRecreate = True

                                 If shouldRecreate Then
                                     Try
                                         Dim oldMsg = CType(Await channel.GetMessageAsync(_lastMessageId), IUserMessage)
                                         If oldMsg IsNot Nothing Then Await oldMsg.DeleteAsync()
                                     Catch
                                     End Try
                                     Dim newMsg = Await channel.SendMessageAsync(embed:=freshEmbed, components:=compBuilder.Build())
                                     _lastMessageId = newMsg.Id
                                 Else
                                     Dim msgToModify = CType(Await channel.GetMessageAsync(_lastMessageId), IUserMessage)
                                     If msgToModify IsNot Nothing Then
                                         Await msgToModify.ModifyAsync(Sub(properties) properties.Embed = freshEmbed)
                                     End If
                                 End If
                             End If
                         Catch ex As Exception
                             Console.WriteLine($"[Auto-Update-Error] {UPDATE_INTERVAL_MINUTES}-Min-Loop failed: {ex.Message}")
                         End Try
                     End Function)
        End Sub
    End Class

End Module
