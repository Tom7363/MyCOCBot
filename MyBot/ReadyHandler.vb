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
                                 Dim statusCommandBuilder As New SlashCommandBuilder() With {
                                 .Name = "status",
                                 .Description = "Displays the resource utilization of the bot, server, and Oracle DB."
                                 }


                                 ' Command Structure setup: /clan-add [tag] [category]
                                 Dim clanAddCmd As New SlashCommandBuilder()
                                 clanAddCmd.WithName("clan-add")
                                 clanAddCmd.WithDescription("Adds a brand new clan target entry to the active guild database layout.")
                                 clanAddCmd.AddOption("tag", ApplicationCommandOptionType.String, "The structural tag id belonging to the clan (e.g. #52LJV8)", isRequired:=True)

                                 ' Establish custom restriction array inputs (Choices dropdown)
                                 Dim categoryOption As New SlashCommandOptionBuilder() With {
                                .Name = "category",
                                .Description = "The functional operating category mapping for this clan tracking stream.",
                                .Type = ApplicationCommandOptionType.String,
                                .IsRequired = True
                                }

                                 categoryOption.AddChoice("FWA", "FWA")
                                 categoryOption.AddChoice("CWL", "CWL")
                                 categoryOption.AddChoice("CWL Backup", "CWL Backup")

                                 clanAddCmd.AddOption(categoryOption)

                                 ' Command Structure setup: /clan-remove [tag]
                                 Dim clanRemoveCmd As New SlashCommandBuilder()
                                 clanRemoveCmd.WithName("clan-remove")
                                 clanRemoveCmd.WithDescription("Removes a registered clan target entry away from the server tracking logs.")
                                 clanRemoveCmd.AddOption("tag", ApplicationCommandOptionType.String, "The specific targeted tracker tag you want dropped.", isRequired:=True)

                                 ' Command Structure setup: /clan-list
                                 Dim clanListCmd As New SlashCommandBuilder()
                                 clanListCmd.WithName("clan-list")
                                 clanListCmd.WithDescription("Displays a comprehensive list of all verified clan entries registered here.")
                                 ' Command Structure setup: /clan-list
                                 Dim dumpListCmd As New SlashCommandBuilder()
                                 dumpListCmd.WithName("dump")
                                 dumpListCmd.WithDescription("Displays a comprehensive list of all clans to dump capital gold.")

                                 ' Command Structure: /cl [clan]
                                 Dim clCmd As New SlashCommandBuilder()
                                 clCmd.WithName("cl")
                                 clCmd.WithDescription("Shows the direct link to join a specific tracked clan.")

                                 ' Important: Set IsAutocomplete = True
                                 clCmd.AddOption("clan", ApplicationCommandOptionType.String, "Type to search for a clan from the database...", isRequired:=True, isAutocomplete:=True)
                                 ' Command Structure: /layout [name]
                                 Dim layoutCmd As New SlashCommandBuilder()
                                 layoutCmd.WithName("layout")
                                 layoutCmd.WithDescription("Displays a stored base layout with both links and image preview.")

                                 ' IsAutocomplete must be set to True
                                 layoutCmd.AddOption("name", ApplicationCommandOptionType.String, "Type to search for a layout...", isRequired:=True, isAutocomplete:=True)


                                 ' Command Structure: /layout-add [name] [coc-link-1] [coc-link-2] [image-link]
                                 Dim layoutAddCmd As New SlashCommandBuilder()
                                 layoutAddCmd.WithName("layout-add")
                                 layoutAddCmd.WithDescription("Adds a new base layout with links and an optional preview image to the database.")

                                 ' Option 1: Name (Required)
                                 layoutAddCmd.AddOption("name", ApplicationCommandOptionType.String, "The name of the layout (e.g., TH16 War Base)", isRequired:=True)

                                 ' Option 2: First CoC Link (Required)
                                 layoutAddCmd.AddOption("coc-link-1", ApplicationCommandOptionType.String, "The primary Clash of Clans copy link", isRequired:=True)

                                 ' Option 3: Second CoC Link (Optional)
                                 layoutAddCmd.AddOption("coc-link-2", ApplicationCommandOptionType.String, "An alternative or backup Clash of Clans copy link", isRequired:=False)

                                 ' Option 4: Image Link (Optional)
                                 layoutAddCmd.AddOption("image-link", ApplicationCommandOptionType.String, "A URL to a screenshot or image of the base layout", isRequired:=False)
                                 ' Option 5: Information notes (Optional)
                                 layoutAddCmd.AddOption("information", ApplicationCommandOptionType.String, "Additional notes or hints for this layout (e.g., Anti-Air, Legend League)", isRequired:=False)
                                 ' /bases
                                 Dim basesCmd = New SlashCommandBuilder() With {
                                     .Name = "bases",
                                     .Description = "Displays an overview of FWA base links for all TH"
                                 }

                                 Await guild.CreateApplicationCommandAsync(basesCmd.Build())

                                 Await guild.CreateApplicationCommandAsync(layoutAddCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(layoutCmd.Build())

                                 Await guild.CreateApplicationCommandAsync(clCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(clanListCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(dumpListCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(clanAddCmd.Build())
                                 Await guild.CreateApplicationCommandAsync(clanRemoveCmd.Build())

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
                                 Await guild.CreateApplicationCommandAsync(statusCommandBuilder.Build())


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
