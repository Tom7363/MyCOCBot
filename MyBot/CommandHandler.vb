Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Discord
Imports Discord.Rest
Imports Discord.Webhook
Imports Discord.WebSocket

Public Class CommandHandler
    Private ReadOnly _client As DiscordSocketClient
    Private ReadOnly LogPfad As String = Path.Combine(AppContext.BaseDirectory, "coc_log.txt")

    ' Constructor to inject the Discord client dependency
    Public Sub New(client As DiscordSocketClient)
        _client = client
    End Sub

    ' The main handler called whenever a slash command is executed
    Public Function HandleSlashCommandAsync(command As SocketSlashCommand) As Task
        ' Offload execution to a background thread to prevent gateway blocking
        Task.Run(Async Function()
                     Try
                         Select Case command.Data.Name.ToLower()
                             Case "ping"
                                 Await HandlePingCommandAsync(command)

                             Case "template"
                                 Await HandleTemplateCommandAsync(command)

                             Case "news"
                                 Await HandleNewsCommandAsync(command)

                             Case "threadembed"
                                 Await HandleThreadEmbedCommandAsync(command)

                             Case "deletethread"
                                 Await HandleDeleteThreadCommandAsync(command)

                             Case "movetothread"
                                 Await HandleMoveToThreadCommandAsync(command)

                             Case "roles"
                                 Await HandleRolesCommandAsync(command)

                             Case "channels"
                                 Await HandleChannelsCommandAsync(command)
                             Case "showclans"
                                 Await HandleShowClansCommandAsync(command)

                             Case Else
                                 Await command.RespondAsync("❌ Unknown command.", ephemeral:=True)
                         End Select
                     Catch ex As Exception
                         Console.WriteLine($"[ERROR] Exception occurred while executing /{command.Data.Name}: {ex.Message}")
                     End Try
                 End Function)

        Return Task.CompletedTask
    End Function

    ' =========================================================================
    ' SLASH COMMAND IMPLEMENTATIONS
    ' =========================================================================

    Private Async Function HandlePingCommandAsync(command As SocketSlashCommand) As Task
        ' -----------------------------------------------------------------
        ' COMMAND: /ping
        ' -----------------------------------------------------------------
        Const Version As String = "01.00.00 C"
        Dim latency As Integer = _client.Latency
        Dim osDescription As String = System.Runtime.InteropServices.RuntimeInformation.OSDescription

        Dim DBConnectionStatus As String = If(OracleDatabaseManager.IsDBConnected(), "Database Connected 🟢", "Database Disconnected 🔴")

        Dim responseMessage As String = $"Hello, I am here V {Version} 🚀{Environment.NewLine}" &
                   $"• **Status:** Online 🟢{Environment.NewLine}" &
                   $"• **Latency:** `{latency} ms`{Environment.NewLine}" &
                   $"• **OS:** `{osDescription}`{Environment.NewLine}" &
                   $"• **DB:** `{DBConnectionStatus}`{Environment.NewLine}" &
                   $"• **System:** `Pak Admin Bot System`"


        Await command.RespondAsync(responseMessage)

        Using writer As New StreamWriter(LogPfad, True)
            Await writer.WriteLineAsync($"[{DateTime.UtcNow}] /ping used by {command.User.Username} (Latency: {latency}ms)")
        End Using
    End Function

    Private Async Function HandleTemplateCommandAsync(command As SocketSlashCommand) As Task
        Dim fileNameOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "filename")

        If fileNameOption IsNot Nothing AndAlso fileNameOption.Value IsNot Nothing Then
            Dim targetFileName As String = fileNameOption.Value.ToString()

            If Not targetFileName.ToLower().EndsWith(".json") Then
                targetFileName &= ".json"
            End If

            Dim avatarUrl As String = command.User.GetAvatarUrl()
            If String.IsNullOrEmpty(avatarUrl) Then avatarUrl = command.User.GetDefaultAvatarUrl()

            Dim placeholders As New Dictionary(Of String, String) From {
            {"{{USERNAME}}", command.User.Username},
            {"{{USER_AVATAR}}", avatarUrl}
        }

            Dim guildChannel As SocketGuildChannel = TryCast(command.Channel, SocketGuildChannel)
            If guildChannel IsNot Nothing Then
                placeholders.Add("{{SERVER_NAME}}", guildChannel.Guild.Name)
                placeholders.Add("{{ROLE_COUNT}}", (guildChannel.Guild.Roles.Count - 1).ToString())
            End If

            ' Variablen für die Auswertung außerhalb des Try-Catch-Blocks
            Dim renderedEmbed As Embed = Nothing
            Dim errorMessage As String = ""
            Dim isError As Boolean = False

            Try
                ' Versuche das Embed zu rendern
                renderedEmbed = EmbedEngine.Render(targetFileName, placeholders)
            Catch ex As System.IO.FileNotFoundException
                errorMessage = $"[Configuration Error] The file `{targetFileName}` was not found in the bot directory."
                isError = True
            Catch ex As System.IO.InvalidDataException
                errorMessage = $"[Format Error] Could not parse `{targetFileName}`. Please verify your JSON syntax."
                isError = True
            Catch ex As Exception
                errorMessage = $"[System Error] Engine failed to render file: {ex.Message}"
                isError = True
            End Try

            ' HIER wirf den Discord-Response – komplett außerhalb von Catch/Finally!
            If isError Then
                Await command.RespondAsync(errorMessage, ephemeral:=True)
            Else
                Await command.RespondAsync(embed:=renderedEmbed, ephemeral:=False)
            End If
        Else
            Await command.RespondAsync("[Error] Please provide a valid JSON filename.", ephemeral:=True)
        End If
    End Function

    Private Async Function HandleNewsCommandAsync(command As SocketSlashCommand) As Task
        ' 1. Parameter auslesen (Kanal und Dateiname)


        Dim targetChannelOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "channel")

        Dim templateFileOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "templatefile")

        Dim errorMessage As String = ""
        Dim successMessage As String = ""
        Dim isError As Boolean = False
        If targetChannelOption IsNot Nothing AndAlso templateFileOption IsNot Nothing Then
            Dim targetChannel As SocketTextChannel = TryCast(targetChannelOption.Value, SocketTextChannel)
            Dim targetFileName As String = templateFileOption.Value.ToString()

            ' Dateiendung absichern
            If Not targetFileName.ToLower().EndsWith(".json") Then targetFileName &= ".json"

            If targetChannel IsNot Nothing Then
                ' 2. Platzhalter für die Engine vorbereiten

                Dim avatarUrl As String = command.User.GetAvatarUrl()
                If String.IsNullOrEmpty(avatarUrl) Then avatarUrl = command.User.GetDefaultAvatarUrl()

                Dim placeholders As New Dictionary(Of String, String) From {
                {"{{USERNAME}}", command.User.Username},
                {"{{USER_AVATAR}}", avatarUrl},
                {"{{SERVER_NAME}}", targetChannel.Guild.Name},
                {"{{CHANNEL_NAME}}", targetChannel.Name}
            }

                ' 3. JSON einlesen und rendern
                Dim renderedEmbed As Embed = Nothing
                Try
                    renderedEmbed = EmbedEngine.Render(targetFileName, placeholders)
                Catch ex As System.IO.FileNotFoundException
                    errorMessage = $"[Configuration Error] Template file `{targetFileName}` not found in templates folder."
                    isError = True
                Catch ex As Exception
                    errorMessage = $"[Template Error] Failed to compile JSON: {ex.Message}"
                    isError = True
                End Try

                ' 4. Wenn das Render erfolgreich war, den Webhook-Prozess starten
                If Not isError Then
                    ' Wir deklarieren Variablen für den Webhook-Client außerhalb, um ihn im Finally sauber zu schließen
                    Dim webhookClient As Discord.Webhook.DiscordWebhookClient = Nothing
                    Dim tempWebhook As IWebhook = Nothing

                    Try
                        ' Prüfen, ob der Bot überhaupt Berechtigungen für Webhooks im Zielkanal hat
                        Dim existingWebhooks = Await targetChannel.GetWebhooksAsync()

                        ' Suchen, ob bereits ein vom Bot erstellter Webhook existiert
                        tempWebhook = existingWebhooks.FirstOrDefault(Function(w) w.Name = "Pak News Webhook")

                        ' Wenn kein Webhook existiert, erstellen wir einen neuen
                        If tempWebhook Is Nothing Then
                            tempWebhook = Await targetChannel.CreateWebhookAsync("Pak News Webhook")
                        End If

                        ' Webhook-Client mit der URL initialisieren
                        ' Webhook-Client sicher über ID und Token initialisieren
                        webhookClient = New DiscordWebhookClient(tempWebhook.Id, tempWebhook.Token)

                        ' Das generierte Embed über den Webhook abschicken
                        ' (Optional: Du kannst hier auch .WithUsername oder .WithAvatarUrl anpassen)
                        Await webhookClient.SendMessageAsync(
                        text:="",
                        embeds:={renderedEmbed},
                        username:="Pak Admin News System",
                        avatarUrl:="https://discordapp.com"
                    )

                        successMessage = $"🚀 Successfully posted news template `{targetFileName}` into <#{targetChannel.Id}> using a secure webhook!"

                    Catch ex As Discord.Net.HttpException When ex.DiscordCode = DiscordErrorCode.InsufficientPermissions
                        errorMessage = "[Permissions Error] The bot lacks 'Manage Webhooks' permission in the target channel."
                        isError = True
                    Catch ex As Exception
                        errorMessage = $"[Webhook Error] Failed to broadcast message: {ex.Message}"
                        isError = True
                    Finally
                        ' Aufräumen: Wenn wir den Webhook-Client genutzt haben, Instanz freigeben
                        If webhookClient IsNot Nothing Then
                            webhookClient.Dispose()
                        End If
                        ' Falls du den Webhook nach jedem Post wieder restlos löschen möchtest, aktiviere diese Zeile:
                        ' If tempWebhook IsNot Nothing Then Await tempWebhook.DeleteAsync()
                    End Try
                End If
            Else
                errorMessage = "[Input Error] Please select a valid text channel."
                isError = True
            End If
        Else
            errorMessage = "[Input Error] Missing required parameters."
            isError = True
        End If

        ' 5. Finale, vom Try-Catch entkoppelte Antwort an den Administrator (ephemeral)
        If isError Then
            Await command.RespondAsync(errorMessage, ephemeral:=True)
        Else
            Await command.RespondAsync(successMessage, ephemeral:=True)
        End If

    End Function

    Private Async Function HandleThreadEmbedCommandAsync(command As SocketSlashCommand) As Task
        Dim gUser = TryCast(command.User, SocketGuildUser)
        If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
            Dim txtChannel = TryCast(command.Channel, SocketTextChannel)
            If txtChannel IsNot Nothing Then
                Dim aktiveThreads = Await txtChannel.GetActiveThreadsAsync()

                Dim embedBuilder As New EmbedBuilder() With {
                                         .Title = $"📂 Thread Overview for #{txtChannel.Name}",
                                         .Description = "Here are the currently active discussion threads in this channel:",
                                         .Color = New Color(52, 152, 219)
                                     }
                embedBuilder.WithCurrentTimestamp()

                If aktiveThreads.Count = 0 Then
                    embedBuilder.Description = "There are currently no active threads in this channel. ❌"
                Else
                    For Each thread As RestThreadChannel In aktiveThreads
                        Dim feldInhalt As String = $"• **Created by:** <@{thread.OwnerId}>{Environment.NewLine}" &
                                                                        $"• **Messages:** `{thread.MessageCount}`{Environment.NewLine}" &
                                                                        $"• **Members:** `{thread.MemberCount}`{Environment.NewLine}" &
                                                                        $"• **Link to Thread:** <#{thread.Id}>"

                        embedBuilder.AddField($"🧵 #{thread.Name}", feldInhalt, inline:=False)
                    Next
                End If

                embedBuilder.WithFooter("Pak Admin Bot System")
                Await command.RespondAsync(embed:=embedBuilder.Build())
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If
    End Function

    Private Async Function HandleDeleteThreadCommandAsync(command As SocketSlashCommand) As Task
        Dim gUser = TryCast(command.User, SocketGuildUser)
        If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
            Dim idStr As String = TryCast(command.Data.Options.First().Value, String)
            Dim threadId As ULong

            If ULong.TryParse(idStr, threadId) Then
                Dim thread = TryCast(_client.GetChannel(threadId), SocketThreadChannel)
                If thread IsNot Nothing Then
                    Dim name As String = thread.Name
                    Await thread.DeleteAsync()
                    Await command.RespondAsync($"The thread **#{name}** was successfully deleted! 🗑️")
                Else
                    Await command.RespondAsync("The thread could not be found.", ephemeral:=True)
                End If
            Else
                Await command.RespondAsync("Invalid Thread ID format.", ephemeral:=True)
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If

    End Function

    Private Async Function HandleMoveToThreadCommandAsync(command As SocketSlashCommand) As Task
        Dim gUser = TryCast(command.User, SocketGuildUser)
        If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
            Dim msgIdStr As String = TryCast(command.Data.Options.Where(Function(o) o.Name = "message_id").First().Value, String)
            Dim threadIdStr As String = TryCast(command.Data.Options.Where(Function(o) o.Name = "thread_id").First().Value, String)

            Dim msgId, threadId As ULong
            If ULong.TryParse(msgIdStr, msgId) AndAlso ULong.TryParse(threadIdStr, threadId) Then
                Dim thread = TryCast(_client.GetChannel(threadId), SocketThreadChannel)
                Dim originalMsg = Await command.Channel.GetMessageAsync(msgId)

                If thread IsNot Nothing AndAlso originalMsg IsNot Nothing Then
                    Dim embedBuilder As New EmbedBuilder() With {
                                             .Author = New EmbedAuthorBuilder() With {.Name = originalMsg.Author.Username, .IconUrl = originalMsg.Author.GetAvatarUrl()},
                                             .Description = originalMsg.Content,
                                             .Color = New Color(230, 126, 34),
                                             .Timestamp = originalMsg.Timestamp
                                         }

                    Await thread.SendMessageAsync(text:="*Moved Message:*", embed:=embedBuilder.Build())
                    Await originalMsg.DeleteAsync()

                    Await command.RespondAsync("Message successfully moved! 📦")
                Else
                    Await command.RespondAsync("Message or Thread could not be found.", ephemeral:=True)
                End If
            Else
                Await command.RespondAsync("Invalid ID format parsed.", ephemeral:=True)
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If
    End Function

    Private Async Function HandleRolesCommandAsync(command As SocketSlashCommand) As Task

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

            embedBuilder.WithFooter("Pak Admin Bot System")

            Await command.RespondAsync(embed:=embedBuilder.Build(), ephemeral:=True)
        End If

    End Function

    Private Async Function HandleChannelsCommandAsync(command As SocketSlashCommand) As Task
        Dim guildUser = TryCast(command.User, SocketGuildUser)

        ' Local variables for error handling out of block scopes
        Dim finalEmbed As Embed = Nothing
        Dim errorMessage As String = ""
        Dim isError As Boolean = False

        If guildUser IsNot Nothing Then
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
        Else
            errorMessage = "This configuration routine can only run inside a server guild context."
            isError = True
        End If

        ' 5. Safely execute asynchronous dispatching (100% immune to BC36943 compiler crashes)
        If isError Then
            Await command.RespondAsync(errorMessage, ephemeral:=True)
        Else
            ' Sends the directory listing privately (ephemeral) so it won't clutter public view
            Await command.RespondAsync(embed:=finalEmbed, ephemeral:=False)
        End If
        'END of command
    End Function

    ' /showclans Implementation
    Private Async Function HandleShowClansCommandAsync(command As SocketSlashCommand) As Task
        ' Verhindert den 3-Sekunden-Timeout von Discord während der DB-Abfrage
        Await command.DeferAsync(ephemeral:=False)

        ' Holt die Clan-Liste aus dem Database-Modul
        Dim clans As List(Of String) = Await OracleDatabaseManager.GetClansAsync()

        ' Erstellt ein optisch ansprechendes Embed im Oracle-Design
        Dim embedBox As New EmbedBuilder() With {
            .Title = "Clash of Clans - Registered Clans",
            .Color = New Color(235, 95, 10), ' Oracle Orange
            .Description = String.Join(Environment.NewLine, clans),
            .Timestamp = DateTimeOffset.Now
        }

        ' Sendet die formatierte Box in den Discord-Kanal
        Await command.FollowupAsync(embed:=embedBox.Build())
    End Function

End Class
