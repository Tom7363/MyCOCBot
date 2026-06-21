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
                                 Await StatusCommands.RegisterPingCommandAsync(_client, guild)
                                 Await StatusCommands.RegisterStatusCommandAsync(_client, guild)

                                 Await DiscordServerCommands.RegisterDiscordServerCommandAsync(_client, guild)

                                 Await TemplateCommands.RegisterTemplateCommandAsync(_client, guild)

                                 Await DiscordThreads.RegisterThreadCommandAsync(_client, guild)

                                 Await ClanManager.RegisterClanmanagerCommandAsync(_client, guild)
                                 Await Baselayouts.RegisterBaseLayoutCommandAsync(_client, guild)
                                 Await CWLRoaster.RegisterCWLCommandAsync(_client, guild)
                                 Await FWAStats_API.RegisterFWAStatsAsync(_client, guild)

                                 Await ChocolateClash_API.RegisterCCAsync(_client, guild)

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
