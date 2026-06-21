Imports System.Text
Imports Discord
Imports Discord.WebSocket
Imports Newtonsoft.Json.Linq

Public Module DiscordserverCmd
    Public Class DiscordServerCommands
        Public Shared Async Function RegisterDiscordServerCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            ' /roles
            Dim rolesCmd = New SlashCommandBuilder() With {
                                     .Name = "roles",
                                     .Description = "Displays the complete sorted server role hierarchy"
                                 }

            ' /channels
            Dim channelsCmd = New SlashCommandBuilder() With {
                                     .Name = "channels",
                                     .Description = "Displays a clean directory tree of all categories and channels"
                                 }

            Dim whoisCommand = New SlashCommandBuilder() With {
                                       .Name = "whois",
                                       .Description = "Finds all Clash of Clans accounts linked to a Discord user."
                                 }
            whoisCommand.AddOption(New SlashCommandOptionBuilder() With {
                    .Name = "user",
                    .Type = ApplicationCommandOptionType.String, ' Changed to String so we can pass the ID via autocomplete
                    .Description = "Type to search a Discord user...",
                    .IsRequired = True,
                    .IsAutocomplete = True
                    })


            Await guild.CreateApplicationCommandAsync(rolesCmd.Build())
            Await guild.CreateApplicationCommandAsync(channelsCmd.Build())
            Await guild.CreateApplicationCommandAsync(whoisCommand.Build())
        End Function


        ''' <summary>
        ''' Resolves a Discord user to all their linked Clash of Clans accounts.
        ''' Fully protected against Discord's 4096 description and 6000 total character limits.
        ''' </summary>
        Public Shared Async Function HandleWhoIsCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
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

        Public Shared Async Function HandleRolesCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task

            Dim guildUser = TryCast(command.User, SocketGuildUser)
            If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
                Dim guild As SocketGuild = guildUser.Guild
                Dim sortierteRollen = guild.Roles.OrderByDescending(Function(r) r.Position).ToList()
                Dim embedBuilder As New EmbedBuilder() With {
                                     .Title = $"🛡️ Role Directory for {guild.Name}",
                                     .Description = $"Total Roles: **{sortierteRollen.Count}**" & Environment.NewLine & "Current Server Hierarchy:",
                                     .Color = New Color(46, 204, 113)
                                 }
                embedBuilder.WithCurrentTimestamp()
                Console.WriteLine("1")
                Dim rollenListe As New StringBuilder()
                For Each rolle As SocketRole In sortierteRollen
                    If rolle.IsEveryone Then Continue For
                    Dim mitgliederAnzahl As Integer = rolle.Members.Count()
                    Dim istManaged As String = If(rolle.IsManaged, "🤖 Bot/System", "👥 User")

                    rollenListe.AppendLine($"• <@&{rolle.Id}> | Users: `{mitgliederAnzahl}` | Type: *{istManaged}*")

                Next

                Dim finalerText As String = rollenListe.ToString()
                If finalerText.Length > 2000 Then
                    embedBuilder.Description &= Environment.NewLine & Environment.NewLine & finalerText.Substring(0, 1900) & "..."
                Else
                    embedBuilder.Description &= Environment.NewLine & Environment.NewLine & finalerText
                End If

                embedBuilder.WithFooter("`Pak Admin Bot System`")

                Await command.RespondAsync(embed:=embedBuilder.Build(), ephemeral:=True)
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If

        End Function
        Public Shared Async Function HandleChannelsCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            Dim guildUser = TryCast(command.User, SocketGuildUser)

            ' Local variables for error handling out of block scopes
            Dim finalEmbed As Embed = Nothing
            Dim errorMessage As String = ""
            Dim isError As Boolean = False

            If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
                Dim guild = guildUser.Guild
                Dim structureText As New StringBuilder()

                ' 1. Collect and process orphan channels (Channels without a Category)
                Dim unassignedChannels = guild.Channels.Where(Function(c)
                                                                  Dim nested = TryCast(c, INestedChannel)
                                                                  Return nested IsNot Nothing AndAlso Not nested.CategoryId.HasValue
                                                              End Function).OrderBy(Function(c) c.Position).ToList()

                If unassignedChannels.Count > 0 Then
                    structureText.AppendLine("**📦 Channels without Category**")
                    For i As Integer = 0 To unassignedChannels.Count - 1
                        Dim targetChannel = unassignedChannels(i)
                        Dim prefix As String = If(i = unassignedChannels.Count - 1, "  └─ ", "  ├─ ")

                        If TypeOf targetChannel Is SocketTextChannel Then
                            structureText.AppendLine($"{prefix}📝 <#{targetChannel.Id}>")
                        ElseIf TypeOf targetChannel Is SocketVoiceChannel Then
                            structureText.AppendLine($"{prefix}🔊 {targetChannel.Name}")
                        Else
                            structureText.AppendLine($"{prefix}⚙️ {targetChannel.Name}")
                        End If
                    Next
                    structureText.AppendLine()
                End If

                ' 2. Loop through all existing Category Modules and their nested channels
                Dim categories = guild.CategoryChannels.OrderBy(Function(c) c.Position).ToList()

                For Each cat In categories
                    structureText.AppendLine($"**📂 {cat.Name.ToUpper()}**")

                    Dim nestedChannels = guild.Channels.Where(Function(c)
                                                                  Dim nested = TryCast(c, INestedChannel)
                                                                  Return nested IsNot Nothing AndAlso nested.CategoryId.HasValue AndAlso nested.CategoryId.Value = cat.Id
                                                              End Function).OrderBy(Function(c) c.Position).ToList()

                    For i As Integer = 0 To nestedChannels.Count - 1
                        Dim targetChannel = nestedChannels(i)
                        Dim prefix As String = If(i = nestedChannels.Count - 1, "  └─ ", "  ├─ ")

                        If TypeOf targetChannel Is SocketTextChannel Then
                            structureText.AppendLine($"{prefix}📝 <#{targetChannel.Id}>")
                        ElseIf TypeOf targetChannel Is SocketVoiceChannel Then
                            structureText.AppendLine($"{prefix}🔊 {targetChannel.Name}")
                        Else
                            structureText.AppendLine($"{prefix}⚙️ {targetChannel.Name}")
                        End If
                    Next
                    structureText.AppendLine()
                Next

                ' 3. Map dynamic parameters into placeholder storage
                Dim compiledTree As String = structureText.ToString()
                If compiledTree.Length > 3900 Then
                    compiledTree = compiledTree.Substring(0, 3850) & vbCrLf & "... (Directory tree truncated due to length limits)"
                End If

                ' WICHTIG: Ersetze echte Zeilenumbrüche mit dem escaped \n String für JSON
                Dim jsonSafeTree As String = compiledTree.Replace(vbCrLf, "\n").Replace(vbLf, "\n").Replace(vbCr, "\n")

                Dim placeholders As New Dictionary(Of String, String) From {
    {"{{SERVER_NAME}}", guild.Name},
    {"{{USERNAME}}", command.User.Username},
    {"{{CHANNEL_TREE}}", jsonSafeTree} ' <-- Hier den bereinigten String nutzen!
}
                ' 4. Safe execution transfer to the custom parsing system
                Try
                    finalEmbed = EmbedEngine.Render("channels_template.json", placeholders)
                Catch ex As System.IO.FileNotFoundException
                    errorMessage = "[Configuration Error] The template layout file `channels_template.json` is missing."
                    isError = True
                Catch ex As Exception
                    errorMessage = $"[System Error] Template engine compilation failure: {ex.Message}"
                    isError = True
                End Try
                ' Sends the directory listing privately (ephemeral) so it won't clutter public view
                Await command.RespondAsync(embed:=finalEmbed, ephemeral:=False)

            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If

            'END of command
        End Function

        Public Shared Async Function HandleWhoIsAutocompleteAsync(interaction As SocketAutocompleteInteraction) As Task
            ' Normalize user input
            Dim userInput As String = If(interaction.Data.Current.Value?.ToString()?.ToLower()?.Trim(), "")

            ' 1. Pull the unique registered users from your Oracle Autonomous DB
            Dim allUsers As List(Of Tuple(Of String, String)) = Await OracleDatabaseManager.GetAllRegisteredUsersAsync()

            ' Failure fallback
            If allUsers Is Nothing OrElse allUsers.Count = 0 Then
                Return
            End If

            ' 2. Ultra-tolerant filter loop
            Dim filteredUsers = allUsers.
            Where(Function(u)
                      Dim dbId As String = u.Item1.ToLower()
                      Dim originalDbName As String = u.Item2.ToLower()

                      ' Convert special fonts (like 𝗧𝗼𝗺 -> tom) for matching
                      Dim cleanDbName As String = DeUnicodeString(originalDbName).ToLower()

                      ' Match against cleaned name, original name, or Discord ID
                      Return cleanDbName.Contains(userInput) OrElse
                             originalDbName.Contains(userInput) OrElse
                             dbId.Contains(userInput)
                  End Function).
            DistinctBy(Function(u) u.Item1).
            Take(25).
            Select(Function(u) New AutocompleteResult($"{u.Item2} ({u.Item1})", u.Item1))

            ' 3. Respond back to Discord client
            Await interaction.RespondAsync(filteredUsers)
        End Function

    End Class

End Module

