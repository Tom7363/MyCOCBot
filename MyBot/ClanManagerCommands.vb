Imports System
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client

Public Module ClanManagerCommands

    ''' <summary>
    ''' Evaluates and saves an added clan via /clan-add [tag] [category]
    ''' </summary>
    Public Async Function HandleClanAddAsync(command As SocketSlashCommand) As Task
        Await command.DeferAsync()
        Dim guildUser = TryCast(command.User, SocketGuildUser)
        If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

            Dim clanTag As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "tag")?.Value.ToString().ToUpper().Replace(" ", "")
            Dim category As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "category")?.Value.ToString()

            If Not clanTag.StartsWith("#") Then clanTag = "#" & clanTag

            ' 1. Check validation through external Supercell API endpoint
            Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

            Dim clanData As JObject = Await cocApi.GetClanDataAsync(clanTag)
            If clanData Is Nothing Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ Clan tag `{clanTag}` could not be found within the Clash of Clans API system.")
                Return
            End If

            Dim clanName As String = clanData("name")?.ToString()

            ' 2. Persist using the database helper functions
            Dim success As Boolean = Await OracleDatabaseManager.AddClanAsync(command.GuildId.Value, clanTag, clanName, category)
            If success Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"✅ **{clanName}** (`{clanTag}`) successfully registered as a **{category}** clan!")
            Else
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ A database operation error occurred while processing the save request.")
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If
    End Function

    ''' <summary>
    ''' Handles removing records via /clan-remove [tag]
    ''' </summary>
    Public Async Function HandleClanRemoveAsync(command As SocketSlashCommand) As Task
        Await command.DeferAsync()
        Dim guildUser = TryCast(command.User, SocketGuildUser)
        If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

            Dim clanTag As String = command.Data.Options.First().Value.ToString().ToUpper().Replace(" ", "")
            If Not clanTag.StartsWith("#") Then clanTag = "#" & clanTag

            Dim success As Boolean = Await OracleDatabaseManager.RemoveClanAsync(command.GuildId.Value, clanTag)
            If success Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"🗑️ Clan `{clanTag}` has been successfully removed from this server's database entries.")
            Else
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ Clan `{clanTag}` was not found or could not be removed from data tables.")
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If
    End Function

    ''' <summary>
    ''' Formats list output grids via /clan-list
    ''' </summary>
    Public Async Function HandleClanListAsync(command As SocketSlashCommand) As Task
        ' Defer response since we are making multiple database and API calls
        Await command.DeferAsync()

        ' 1. Fetch tracked clans from Oracle DB
        Dim clans = Await OracleDatabaseManager.GetClansAsync(command.GuildId.Value)

        If clans.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No clans are registered to this server database. Use `/clan-add` to begin.")
            Return
        End If

        ' Lists to separate types dynamically
        Dim cwlClans As New List(Of Tuple(Of String, String))()
        Dim fwaClans As New List(Of Tuple(Of String, String))()
        Dim backupClans As New List(Of Tuple(Of String, String))()

        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

        ' 2. Categorize and fetch live member counts from Supercell API
        For Each clan In clans
            Dim tag As String = clan.Item1
            Dim name As String = clan.Item2
            Dim cat As String = clan.Item3

            Dim memberCount As String = "N/A"


            ' Fetch live member count from API
            Dim clanData As JObject = Await cocApi.GetClanDataAsync(tag)
            If clanData IsNot Nothing AndAlso clanData("members") IsNot Nothing Then
                memberCount = clanData("members").ToString()
            End If

            ' Create the official CoC Deep Link (remove '#' for the URL query parameter)
            Dim cleanTagForUrl As String = tag.Replace("#", "%23")
            Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTagForUrl}"
            ' Format the line using Discord Markdown link style: [Text](URL)
            Dim formattedLine As String = $"[{name} ({tag})]({cocDeepLink}) - {memberCount}"

            ' Group into respective categories
            If cat.Equals("CWL", StringComparison.OrdinalIgnoreCase) Then
                cwlClans.Add(Tuple.Create(name, formattedLine))
            ElseIf cat.Equals("FWA", StringComparison.OrdinalIgnoreCase) Then
                fwaClans.Add(Tuple.Create(name, formattedLine))
            Else
                backupClans.Add(Tuple.Create(name, formattedLine))
            End If
        Next

        ' Sort lists by Clan Name to maintain clean alphabetized appearance
        cwlClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))
        fwaClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))
        backupClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))

        ' 3. Build the Discord layout output matching your design
        Dim embed As New EmbedBuilder() With {
            .Title = "PAK: 💎FWA💎 Clans",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }

        Dim description As New StringBuilder()

        ' Append CWL Section
        If cwlClans.Count > 0 Then
            description.AppendLine("**CWL**")
            For Each cwl In cwlClans
                description.AppendLine(cwl.Item2)
            Next
            description.AppendLine() ' Spacer line
        End If

        ' Append FWA Section
        If fwaClans.Count > 0 Then
            description.AppendLine("**FWA**")
            For Each fwa In fwaClans
                description.AppendLine(fwa.Item2)
            Next
            description.AppendLine() ' Spacer line
        End If

        ' Append CWL Backup Section (if any exist)
        If backupClans.Count > 0 Then
            description.AppendLine("**CWL Backup**")
            For Each backup In backupClans
                description.AppendLine(backup.Item2)
            Next
        End If

        embed.WithDescription(description.ToString())
        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText($"Total : {clans.Count} Clans"))

        ' Send final formatted layout back to Discord
        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function

    ''' <summary>
    ''' Formats list output grids via /dump filtering clans where DUMP is '1'
    ''' </summary>
    Public Async Function HandleDumpListAsync(command As SocketSlashCommand) As Task
        Await command.DeferAsync()

        ' 1. Fetch tracked clans from Oracle DB
        Dim clans As List(Of Dictionary(Of String, String)) = Await OracleDatabaseManager.GetDumpClansAsync(command.GuildId.Value.ToString())

        If clans Is Nothing OrElse clans.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No clans are marked for dump in this server database.")
            Return
        End If

        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
        Dim description As New StringBuilder()

        ' 2. Build the list with correct URL formatting
        For Each clan In clans
            Dim tag As String = clan("tag")
            Dim name As String = clan("name")
            Dim memberCount As String = "N/A"

            ' Fetch live member count from API
            Dim clanData As Newtonsoft.Json.Linq.JObject = Await cocApi.GetClanDataAsync(tag)
            If clanData IsNot Nothing AndAlso clanData("members") IsNot Nothing Then
                memberCount = clanData("members").ToString()
            End If

            ' FIX 1: Use the official Deep Link structure and escape the '#' properly
            Dim cleanTagForUrl As String = tag.Replace("#", "%23")
            Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTagForUrl}"

            ' FIX 2: Correct Discord Markdown formatting: • [Clan Name (#Tag)](URL) - Members
            description.AppendLine($"• [{name} ({tag})]({cocDeepLink}) - {memberCount}")
        Next

        ' 3. Build and send the final Embed
        Dim embed As New EmbedBuilder() With {
            .Title = "PAK: Registered Clans (Dump)",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }

        embed.WithDescription(description.ToString())
        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText($"Total: {clans.Count} Clans"))

        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function
    Public Async Function HandleClCommandAsync(command As SocketSlashCommand) As Task
        Await command.DeferAsync()

        ' The value received here is the clan tag (e.g., #2JLJVYQPU) passed from Autocomplete
        Dim clanTag As String = command.Data.Options.First().Value.ToString().ToUpper().Trim()

        ' Open connection to check current stats if necessary
        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
        Dim clanData = Await cocApi.GetClanDataAsync(clanTag)

        Dim clanName As String = "Unknown Clan"
        Dim memberCount As String = "N/A"

        If clanData IsNot Nothing Then
            clanName = clanData("name")?.ToString()
            memberCount = clanData("members")?.ToString()
        End If

        ' Official working Supercell web link redirect
        Dim cleanTag As String = clanTag.Replace("#", "")
        Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTag}"

        ' Build simple clean UI layout response
        Dim embed As New EmbedBuilder() With {
            .Title = $"🔗 Join Link: {clanName}",
            .Color = Color.Green,
            .Timestamp = DateTimeOffset.Now
        }

        embed.WithDescription($"Click the link below to view or join **{clanName}** inside the Clash of Clans app!{Environment.NewLine}{Environment.NewLine}" &
                              $"👉 **[Open Profile & Join ({clanTag})]({cocDeepLink})**")

        embed.AddField("Current Status", $"👥 **Members:** {memberCount}/50", inline:=True)
        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText("`Pak Admin Bot System`"))

        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function

    ''' <summary>
    ''' Resolves a Discord user to all their linked Clash of Clans accounts.
    ''' Fully protected against Discord's 4096 description and 6000 total character limits.
    ''' </summary>
    Public Async Function HandleWhoIsCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
        ' Defer response immediately since we are querying both Oracle DB and the external Supercell API
        Await command.DeferAsync()

        Dim rawInput As String = command.Data.Options.First().Value.ToString().Trim()
        Dim targetIdStr As String = ""
        Dim targetUserId As ULong

        ' =========================================================================
        ' PHASE 1: USER RESOLUTION (Autocomplete ID vs Manual Typing)
        ' =========================================================================
        If ULong.TryParse(rawInput, targetUserId) Then
            ' Case A: Selected from Autocomplete list (rawInput is a valid numeric Discord ID)
            targetIdStr = targetUserId.ToString()
        Else
            ' Case B: Typed a name manually (e.g., "Tom") instead of clicking the list
            ' We search the Oracle Database live for a matching name to find their real Discord ID
            targetIdStr = Await OracleDatabaseManager.GetIdByNameAsync(rawInput)
            If String.IsNullOrEmpty(targetIdStr) OrElse Not ULong.TryParse(targetIdStr, targetUserId) Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ Could not find any registered user matching `{rawInput}` in the database. Please use the autocomplete list.")
                Return
            End If
        End If

        ' Fetch the user profile from Discord. First check local cache, fallback to REST API (crucial for Cloud VMs)
        Dim targetUser As IUser = client.GetUser(targetUserId)
        If targetUser Is Nothing Then
            Try
                ' Fetches user profile live from Discord servers over HTTP
                targetUser = Await client.Rest.GetUserAsync(targetUserId)
            Catch ex As Exception
                Console.WriteLine($"[DISCORD API WARNING] Failed to fetch REST profile for ID {targetUserId}: {ex.Message}")
            End Try
        End If

        ' =========================================================================
        ' PHASE 2: DATABASE LOOKUP (Fetch linked accounts from Oracle Autonomous DB)
        ' =========================================================================
        Dim linkedAccounts As List(Of Tuple(Of String, String, String)) = Await OracleDatabaseManager.GetLinkedAccountsAsync(targetIdStr)

        If linkedAccounts Is Nothing OrElse linkedAccounts.Count = 0 Then
            Dim fallbackName As String = If(targetUser IsNot Nothing, targetUser.Username, rawInput)
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"ℹ️ **{fallbackName}** does not have any Clash of Clans accounts linked to this server's database.")
            Return
        End If

        ' Visual indicator formatting


        ' =========================================================================
        ' PHASE 3: PARALLEL API REQUESTS & MULTI-EMBED CONSTRUCTION
        ' =========================================================================
        Dim embedTitle As String = If(targetUser IsNot Nothing, If(targetUser.GlobalName, targetUser.Username), rawInput)
        Dim userAvatarUrl As String = If(targetUser IsNot Nothing, If(targetUser.GetAvatarUrl(), targetUser.GetDefaultAvatarUrl()), Nothing)

        Dim embedList As New List(Of Embed)()

        Dim currentEmbed As New EmbedBuilder() With {
            .Title = $"📋 Account Profile: {embedTitle}",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }
        If Not String.IsNullOrEmpty(userAvatarUrl) Then currentEmbed.WithThumbnailUrl(userAvatarUrl)

        Dim headerText As New StringBuilder()
        headerText.AppendLine($"**Discord Account:** {If(targetUser IsNot Nothing, targetUser.Mention, "`" & rawInput & "`")}")
        headerText.AppendLine()
        currentEmbed.WithDescription(headerText.ToString())

        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
        Dim apiTasks As New List(Of Task(Of Tuple(Of String, String, String, String)))()

        For Each account In linkedAccounts
            Dim tag As String = account.Item1
            Dim dbName As String = account.Item2
            Dim individualVerified As String = account.Item3

            ' FIX 1: Wenn es sich um das Dummy-Tag handelt, überspringen wir die Supercell-API komplett
            If tag = "#0" Then
                ' Wir fügen einen sofort abgeschlossenen Task hinzu, um das System parallel zu halten
                apiTasks.Add(Task.FromResult(Tuple.Create("#0", "No Account", "No Clan", individualVerified)))
            Else
                ' Echter Account: Wird wie gewohnt parallel im Hintergrund abgefragt
                Dim fetchTask = Task.Run(Async Function() As Task(Of Tuple(Of String, String, String, String))
                                             Dim livePlayerName As String = dbName
                                             Dim liveClanName As String = "No Clan"
                                             Try
                                                 Dim playerData As JObject = Await cocApi.GetPlayerDataAsync(tag)
                                                 If playerData IsNot Nothing Then
                                                     If playerData("name") IsNot Nothing Then livePlayerName = playerData("name").ToString()
                                                     If playerData("clan") IsNot Nothing AndAlso playerData("clan")("name") IsNot Nothing Then
                                                         liveClanName = playerData("clan")("name").ToString()
                                                     End If
                                                 End If
                                             Catch
                                                 liveClanName = "⚠️ Live API Error"
                                             End Try
                                             Return Tuple.Create(tag, livePlayerName, liveClanName, individualVerified)
                                         End Function)
                apiTasks.Add(fetchTask)
            End If
        Next

        ' Alle Tasks abwarten (die #0 Tasks sind sofort fertig)
        Dim completedResults As Tuple(Of String, String, String, String)() = Await Task.WhenAll(apiTasks)

        Dim currentChunk As New StringBuilder()
        Dim totalEstimatedLength As Integer = currentEmbed.Title.Length + headerText.Length
        Dim fieldIndex As Integer = 1

        For Each result In completedResults
            Dim tag As String = result.Item1
            Dim livePlayerName As String = result.Item2
            Dim liveClanName As String = result.Item3
            Dim individualVerified As String = result.Item4

            Dim accountLine As String = ""

            ' FIX 2: Visuelle Ausgabe für Dummy-Accounts anpassen (Kein Link, sauberer Text)
            If tag = "#0" Then
                accountLine = "• *No Clash of Clans account linked to this profile.*" & Environment.NewLine
            Else
                ' Normaler Account mit funktionierendem Deep Link
                Dim verifiedEmoji As String = If(individualVerified.Equals("Yes", StringComparison.OrdinalIgnoreCase), " ✅", " ❌")
                Dim cleanTagForUrl As String = tag.Replace("#", "").ToUpper()
                Dim playerDeepLink As String = $"https://link.clashofclans.com/en?action=OpenPlayerProfile&tag={cleanTagForUrl}"

                accountLine = $"• [{livePlayerName} ({tag})]({playerDeepLink}) | **{liveClanName}**{verifiedEmoji}" & Environment.NewLine
            End If

            ' Zeichenbegrenzungs-Check für Discord
            If (currentChunk.Length + accountLine.Length > 950) OrElse (totalEstimatedLength + currentChunk.Length + accountLine.Length > 5200) Then
                If currentChunk.Length > 0 Then
                    Dim titleText As String = If(fieldIndex = 1, "**Linked Clash of Clans Accounts:**", $"**Linked Accounts (Part {fieldIndex}):**")
                    currentEmbed.AddField(titleText, currentChunk.ToString(), inline:=False)
                    fieldIndex += 1
                End If

                If totalEstimatedLength + currentChunk.Length > 5200 Then
                    embedList.Add(currentEmbed.Build())
                    currentEmbed = New EmbedBuilder() With {
                        .Title = $"📋 Account Profile: {embedTitle} (Continued)",
                        .Color = Color.Blue
                    }
                    totalEstimatedLength = currentEmbed.Title.Length
                Else
                    totalEstimatedLength += currentChunk.Length + 50
                End If

                currentChunk = New StringBuilder()
            End If

            currentChunk.Append(accountLine)
        Next

        ' Restliche Accounts anhängen
        If currentChunk.Length > 0 Then
            Dim titleText As String = If(fieldIndex = 1, "**Linked Clash of Clans Accounts:**", $"**Linked Accounts (Part {fieldIndex}):**")
            currentEmbed.AddField(titleText, currentChunk.ToString(), inline:=False)
        End If

        currentEmbed.WithFooter(New EmbedFooterBuilder().WithText($"Total Accounts: {linkedAccounts.Where(Function(a) a.Item1 <> "#0").Count()} | Powered by Oracle Cloud"))
        embedList.Add(currentEmbed.Build())



        ' =========================================================================
        ' PHASE 4: SEND RESPONSE (Splits messages into separate HTTP requests)
        ' =========================================================================
        If embedList.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ Error processing profile formatting.")
            Return
        End If

        ' 1. Send the FIRST main embed as the original interaction response
        Await command.ModifyOriginalResponseAsync(Sub(p)
                                                      p.Embed = embedList(0)
                                                  End Sub)

        ' 2. If there are more embeds (Continued parts), send them as independent Follow-up messages
        ' This bypasses Discord's combined 6000 character restriction completely!
        If embedList.Count > 1 Then
            For i As Integer = 1 To embedList.Count - 1
                Try
                    ' Sends each continuing embed block as a separate message stream
                    Await command.FollowupAsync(embed:=embedList(i))
                    ' Small safety delay to prevent Discord API rate-limiting on your OCI Instance
                    Await Task.Delay(200)
                Catch ex As Exception
                    Console.WriteLine($"[DISCORD API ERROR] Failed to send followup embed part {i}: {ex.Message}")
                End Try
            Next
        End If
    End Function
    ''' <summary>
    ''' Fetches all members of FWA clans and dynamically creates a custom-named table.
    ''' </summary>
    Public Async Function HandleRosterCreateAsync(command As SocketSlashCommand) As Task
        ' 1. Sichert das 3-Sekunden-Zeitfenster bei Discord
        Await command.DeferAsync()
        API_COC.DebugPrint("[ROSTER] Execution triggered via /roster-create.")

        ' 2. WEIGHTS-Tabelle vorbereiten und FWA-Basis-Gewichte in den RAM laden
        Dim fwaJson As JObject = Await OracleDatabaseManager.InitializeWeightsTableAsync()

        If fwaJson Is Nothing Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ Failed to connect to the database or download FWA Stats.")
            Return
        End If

        ' --- DEINE EXISTIERENDE CLAN/SPIELER LOGIK ---
        ' Hier liest du die Spieler-Daten aus (z.B. über ein Clan-Tag aus deinen Slash-Command-Optionen oder der DB)
        ' Nehmen wir an, wir holen uns eine Liste von Spielern aus deiner Clash of Clans API Schnittstelle:

        ' BEISPIEL-DATEN (Ersetze dies durch deine echte Schleife deiner CoC API-Klassen):
        Dim samplePlayers As New Dictionary(Of String, String) From {
            {"#Y9V2YV2", "16"},
            {"#2G2Y8YL", "15"},
            {"#P8RR92V", "14"}
        }

        Dim playersProcessed As Integer = 0

        ' Schleife durch alle Spieler des zu erstellenden Rosters
        For Each player In samplePlayers
            Dim playerTag As String = player.Key      ' z.B. "#Y9V2YV2"
            Dim townHall As String = player.Value     ' z.B. "16"
            Dim jsonKey As String = "TH" & townHall   ' Erzeugt "TH16"

            ' Gewicht aus den geladenen FWA Stats herausfiltern
            Dim calculatedWeight As Integer = 0
            Dim thDetails = fwaJson(jsonKey)

            If thDetails IsNot Nothing Then
                If thDetails.HasValues Then
                    ' Falls Min/Max-Struktur existiert, nutzen wir das maximale Richtgewicht
                    calculatedWeight = If(thDetails("Max")?.Value(Of Integer)(), If(thDetails("max")?.Value(Of Integer)(), 0))
                Else
                    calculatedWeight = thDetails.Value(Of Integer)()
                End If
            End If

            ' 3. SPIELER-EINTRAG IN DER DB SPEICHERN
            ' Füllt das Feld WEIGHT basierend auf dem referenzierten Player-Tag
            Await OracleDatabaseManager.SavePlayerWeightToDbAsync(playerTag, calculatedWeight, jsonKey)
            playersProcessed += 1
        Next

        API_COC.DebugPrint($"[ROSTER SUCCESS] Maintained {playersProcessed} player weight records.")

        ' 4. Rich Embed Antwort an den Discord-Kanal senden
        Dim embed As New EmbedBuilder() With {
            .Title = "FWA Roster-Gewichte aktualisiert",
            .Description = $"Die Tabelle **WEIGHTS** wurde erfolgreich aktualisiert und mit den individuellen Spieler-Gewichten befüllt.",
            .Color = Color.Green
        }
        embed.AddField("Verarbeitete Accounts", playersProcessed.ToString(), True)
        embed.WithFooter("Datenbasis: fwastats.com", "https://fwastats.com")

        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function
End Module
