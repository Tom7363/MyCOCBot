Imports System
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket

Public Class ReadyHandler
    Private ReadOnly _client As DiscordSocketClient

    ' Constructor to inject the Discord client dependency
    Public Sub New(client As DiscordSocketClient)
        _client = client
    End Sub

    ' The main ready handler called once the bot successfully connects to Discord
    Public Function HandleClientReadyAsync() As Task
        Dim backgroundIPCheck = Task.Run(Async Function()
                                             Await StartPeriodicIPUpdateLoopAsync()
                                         End Function)

        ' Offload the entire registration to a background thread immediately
        Task.Run(Async Function()
                     Try
                         Console.WriteLine("[SYSTEM] Starting slash command registration...")

                         For Each guild As SocketGuild In _client.Guilds
                             Try
                                 ' /ping
                                 Dim pingCmd = New SlashCommandBuilder() With {
                                     .Name = "ping",
                                     .Description = "Check connection, bot version, and live latency"
                                 }

                                 ' /threadembed
                                 Dim threadEmbedCmd = New SlashCommandBuilder() With {
                                     .Name = "threadembed",
                                     .Description = "Displays an overview of all active threads (Requires Server Orga)"
                                 }

                                 ' /deletethread
                                 Dim deleteThreadCmd = New SlashCommandBuilder() With {
                                     .Name = "deletethread",
                                     .Description = "Permanently deletes a thread via its ID (Requires Server Orga)"
                                 }.AddOption("id", ApplicationCommandOptionType.String, "The exact ID of the thread to delete", isRequired:=True)

                                 ' /movetothread
                                 Dim moveThreadCmd = New SlashCommandBuilder() With {
                                     .Name = "movetothread",
                                     .Description = "Moves a message from the channel into a specific thread (Requires Server Orga)"
                                 }.AddOption("message_id", ApplicationCommandOptionType.String, "The ID of the message to move", isRequired:=True) _
                                  .AddOption("thread_id", ApplicationCommandOptionType.String, "The ID of the target thread", isRequired:=True)

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
                                 ' /template
                                 Dim templCmd = New SlashCommandBuilder() With {
                                    .Name = "template",
                                    .Description = "Renders and posts an embed layout directly from a JSON file"
                                 }.AddOption("filename", ApplicationCommandOptionType.String, "The filename of the JSON template (e.g., embed_template.json)", isRequired:=True)
                                 ' /news
                                 Dim newsCmd = New SlashCommandBuilder() With {
                                .Name = "news",
                                .Description = "Posts a JSON template as a webhook into a specific channel"
                                 }.AddOption("channel", ApplicationCommandOptionType.Channel, "The target channel for the news post", isRequired:=True) _
                                 .AddOption("templatefile", ApplicationCommandOptionType.String, "The filename of the JSON layout inside the templates folder", isRequired:=True)

                                 ' /showclans
                                 Dim showClansCmd = New SlashCommandBuilder() With {
                                     .Name = "showclans",
                                     .Description = "Displays all registered Clash of Clans clans from the Oracle database"
                                 }

                                 ' Zusammen mit den anderen Commands an Discord senden
                                 Await guild.CreateApplicationCommandAsync(showClansCmd.Build())


                                 ' Füge diese Zeile zu den anderen Creates hinzu
                                 Await guild.CreateApplicationCommandAsync(newsCmd.Build())

                                 ' Send to Discord API asynchronously
                                 Await guild.CreateApplicationCommandAsync(pingCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(threadEmbedCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(deleteThreadCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(moveThreadCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(rolesCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(channelsCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(templCmd.Build())

                                 Console.WriteLine($"[SYSTEM] Slash commands successfully registered on guild: {guild.Name}")
                             Catch ex As Exception
                                 Console.WriteLine($"[ERROR] Failed to register commands on guild {guild.Name}: {ex.Message}")
                             End Try
                         Next

                         Console.WriteLine("[SYSTEM] Slash command registration finished.")
                     Catch ex As Exception
                         Console.WriteLine($"[CRITICAL] Error in Ready Background Task: {ex.Message}")
                     End Try
                 End Function)

        ' Release the gateway task instantly
        Return Task.CompletedTask
    End Function

    ''' <summary>
    ''' This is the actual event handler that Discord.Net calls when the bot logs in successfully.
    ''' </summary>
    Private Async Function ReadyAsyncHandler() As Task
        Try
            Console.WriteLine("Bot is connected and ready!")
            API_COC.DebugPrint("Discord Bot successfully connected. Starting background tasks.")

            ' 1. Start the periodic IP check task in the background (Non-blocking)
            ' Task.Run ensures this loop doesn't freeze the main Discord thread.
            Dim backgroundIPCheck = Task.Run(Async Function()
                                                 Await StartPeriodicIPUpdateLoopAsync()
                                             End Function)

            ' 2. (Optional) Here you can register your Slash Commands like /showclans or /ping
            ' Await RegisterSlashCommandsAsync()

        Catch ex As Exception
            Console.WriteLine("[ERROR] Exception inside ReadyAsyncHandler: " & ex.Message)
            API_COC.DebugPrint("Critical failure in ReadyAsyncHandler: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' An infinite loop that triggers the Supercell API key renewal every single hour.
    ''' </summary>
    Private Async Function StartPeriodicIPUpdateLoopAsync() As Task
        Dim retryDelay As Integer = 3600000 ' 1hour

        While True
            Try
                ' Wait for exactly 1 hour (3,600,000 milliseconds)
                ' Use Task.Delay, NEVER use Thread.Sleep in async methods!
                Await Task.Delay(retryDelay)

                API_COC.DebugPrint("Periodic background IP and token check initiated.")
                Console.WriteLine("Running hourly background IP validation...")

                ' Execute your automated class method
                Dim updateSuccessful As Boolean = Await API_COC.UpdateKeysAsync()

                If updateSuccessful Then
                    API_COC.DebugPrint("Periodic background update finished. Token is valid.")
                Else
                    API_COC.DebugPrint("⚠️ Periodic background update failed! Checking internet connectivity.")
                End If
                retryDelay = 3600000
            Catch ex As Exception
                API_COC.DebugPrint("Exception caught during periodic loop execution: " & ex.Message)
                ' Wait 5 minutes before retrying if a severe network crash occurs
                retryDelay = 300000
            End Try
        End While
    End Function

End Class
