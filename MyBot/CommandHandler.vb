Imports System
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Discord
Imports Discord.Rest
Imports Discord.Webhook
Imports Discord.WebSocket
Imports HtmlAgilityPack
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Public Class CommandHandler
    Private ReadOnly _client As DiscordSocketClient
    Private ReadOnly LogPfad As String = Path.Combine(AppContext.BaseDirectory, "coc_log.txt")

    ' Constructor to inject the Discord client dependency
    Public Sub New(client As DiscordSocketClient)
        _client = client
    End Sub

    ' English Code Comments
    ''' <summary>
    ''' The main handler called natively by Discord.Net whenever a slash command is executed.
    ''' Cleaned to use native Async/Await to prevent Linux-ARM64 thread spinlocks (100% CPU bug).
    ''' </summary>
    Public Async Function HandleSlashCommandAsync(command As SocketSlashCommand) As Task
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
                    Await HandleClanListAsync(command)

                Case "status"
                    ' Add Await here since it pulls live data from Oracle DB
                    Dim embedBuilder = Await GetResourceUsageEmbedAsync(_client)
                    ' Send response to user
                    Await command.RespondAsync(embed:=embedBuilder.Build())
                Case "clan-add"
                    Await ClanManagerCommands.HandleClanAddAsync(command)
                Case "clan-remove"
                    Await ClanManagerCommands.HandleClanRemoveAsync(command)
                Case "clan-list"
                    Await ClanManagerCommands.HandleClanListAsync(command)
                Case "dump"
                    Await ClanManagerCommands.HandleDumpListAsync(command)
                Case "whois"
                    Await ClanManagerCommands.HandleWhoIsCommandAsync(command, _client)

                Case "cl"
                    Await ClanManagerCommands.HandleClCommandAsync(command)

                Case "layout"
                    Await HandleLayoutCommandAsync(command)
                Case "bases"
                    Await HandleBasesCommandAsync(command)
                Case "layout-add"
                    Await HandleLayoutAddAsync(command)
                Case "roaster-create"
                    Await HandleRosterCreateAsync(command)
                Case "weight-update"
                    Await HandleWeigthUpdateAsync(command)
                Case "cc"
                    ' 1. Acknowledge the command instantly (crucial since FlareSolverr bypasses might take a few seconds)
                    Await command.DeferAsync()

                    ' 2. Retrieve the value of the "clantag" option safely
                    Dim clanTagOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "clantag")
                    If clanTagOption IsNot Nothing Then
                        Dim clanTag As String = clanTagOption.Value.ToString()

                        ' 1. Call the parsing engine and store the extracted winstring result
                        Dim predictionResult As String = Await ChocolateClashAPI.GetChocolateClanWarAsync(clanTag)

                        If Not String.IsNullOrEmpty(predictionResult) Then
                            ' 2. Determine the embed sidebar color based on the prediction keywords
                            Dim embedColor As Color = Color.LightGrey ' Default fallback color

                            Dim lowercaseResult As String = predictionResult.ToLower()
                            If lowercaseResult.Contains("should win") OrElse lowercaseResult.Contains("winner") Then
                                embedColor = Color.Green ' Clan is predicted to win
                            ElseIf lowercaseResult.Contains("should lose") OrElse lowercaseResult.Contains("loser") Then
                                embedColor = Color.Red ' Clan is predicted to lose
                            ElseIf lowercaseResult.Contains("draw") OrElse lowercaseResult.Contains("tie") Then
                                embedColor = Color.Gold ' Tie/Draw scenario
                            End If

                            ' 3. Construct the stylized Discord Embed interface card
                            Dim embed = New EmbedBuilder() With {
                                .Title = "📊 FWA War Live Prediction",
                                .Description = $"Live calculation data fetched successfully from the farming network pipeline.",
                                .Color = embedColor,
                                .Timestamp = DateTimeOffset.Now
                            }

                            ' Add clean structured field sections to the card layout
                            embed.AddField("Target Clan Tag", $"`#{clanTag}`", inline:=True)
                            embed.AddField("Prediction Outcome", $"**{predictionResult}**", inline:=False)

                            ' Optional visual styling add-on anchors
                            embed.WithFooter(footer:=New EmbedFooterBuilder() With {
                                .Text = "Oracle Cloud Ampere Node • FlareSolverr Core"
                            })

                            ' 4. Push the fully rendered rich embed card straight back onto the user's Discord interface
                            Await command.FollowupAsync(embed:=embed.Build())
                        Else
                            ' Fallback if the layout parsing node structure failed on the VM disk partition
                            Await command.FollowupAsync($"⚠️ **Warning:** The bypass completed, but no layout prediction strings could be parsed for Clan Tag `#{clanTag}`.")
                        End If
                    Else
                        Await command.FollowupAsync("Error: No Clan Tag parameter was provided by the client wrapper framework.")
                    End If
                Case Else
                    Await command.RespondAsync("❌ Unknown command.", ephemeral:=True)
            End Select
        Catch ex As Exception
            ' Log the error safely using thread-yielding async file operations
            Dim errorLog As String = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [CRITICAL] Command execution failed: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            Console.WriteLine(errorLog)
            Try
                File.AppendAllText(LogPfad, errorLog & Environment.NewLine)
            Catch
                ' Ignore if log file is locked by the OS filesystem
            End Try
        End Try
        Return
    End Function
    ' Füge diese beiden Zeilen ein, um den fehlenden Client bereitzustellen:
    '' <summary>
    ''' Processes the creation of a new base layout entry via /layout-add
    ''' </summary>
    Public Async Function HandleLayoutAddAsync(command As SocketSlashCommand) As Task
        Dim gUser = TryCast(command.User, SocketGuildUser)
        If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

            Await command.DeferAsync()

            ' Extract parameters from the options list
            Dim layoutName As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "name")?.Value.ToString().Trim()
            Dim cocLink1 As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "coc-link-1")?.Value.ToString().Trim()
            Dim cocLink2 As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "coc-link-2")?.Value.ToString()?.Trim()
            Dim imageLink As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "image-link")?.Value.ToString()?.Trim()

            ' Quick structural check for the main CoC link
            If Not cocLink1.StartsWith("https://link.clashofclans.com", StringComparison.OrdinalIgnoreCase) Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ The primary link must be a valid Clash of Clans share link (`https://link.clashofclans.com/...`).")
                Return
            End If

            ' Quick structural check for the backup CoC link (if provided)
            If Not String.IsNullOrEmpty(cocLink2) AndAlso Not cocLink2.StartsWith("https://link.clashofclans.com", StringComparison.OrdinalIgnoreCase) Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ The backup link must be a valid Clash of Clans share link (`https://link.clashofclans.com/...`).")
                Return
            End If
            ' Extract parameters from the options list (add this below image-link extraction)
            Dim layoutInfo As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "information")?.Value.ToString()?.Trim()

            ' Update the DB insert call parameter list at the bottom:
            Dim success As Boolean = Await OracleDatabaseManager.AddBaseLayoutAsync(command.GuildId.Value, layoutName, cocLink1, cocLink2, imageLink, layoutInfo)

            '
            If success Then
                Dim embed As New EmbedBuilder() With {
                .Title = "✅ Base Layout Added Successfully",
                .Color = Color.Green,
                .Timestamp = DateTimeOffset.Now
            }
                embed.AddField("Layout Name", layoutName, inline:=False)
                embed.AddField("Primary Link", $"[Click here to view link]({cocLink1})", inline:=True)

                If Not String.IsNullOrEmpty(cocLink2) Then
                    embed.AddField("Backup Link", $"[Click here to view link]({cocLink2})", inline:=True)
                End If
                ' Add a field inside the success embed preview:
                If Not String.IsNullOrEmpty(layoutInfo) Then
                    embed.AddField("Notes / Information", layoutInfo, inline:=False)
                End If
                ' Set preview thumbnail or main image if provided
                If Not String.IsNullOrEmpty(imageLink) Then
                    embed.WithThumbnailUrl(imageLink)
                End If

                embed.WithFooter(footer:=New EmbedFooterBuilder().WithText("Stored securely in Oracle Autonomous Database"))

                Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
            Else
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ A database exception occurred while saving the base layout.")
            End If
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If

    End Function
    Public Async Function HandleLayoutCommandAsync(command As SocketSlashCommand) As Task
        Await command.DeferAsync()

        ' The value received from Autocomplete is the layout_id
        Dim layoutIdStr As String = command.Data.Options.First().Value.ToString()
        Dim layoutId As Integer

        If Not Integer.TryParse(layoutIdStr, layoutId) Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ Please select a layout from the autocomplete list preview.")
            Return
        End If

        ' Fetch details from Oracle DB
        Dim layoutData = Await OracleDatabaseManager.GetLayoutDetailsAsync(layoutId)

        If layoutData.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ Layout could not be found in the database.")
            Return
        End If

        ' Build Layout Embed
        Dim embed As New EmbedBuilder() With {
            .Title = $"🏰 Base Layout: {layoutData("name")}",
            .Color = Color.Purple,
            .Timestamp = DateTimeOffset.Now
        }

        ' Format Description with both Links
        Dim desc As New Text.StringBuilder()
        ' Show information notes right at the top of the description if present
        If Not String.IsNullOrEmpty(layoutData("info")) Then
            desc.AppendLine($"📝 *{layoutData("info")}*")
            desc.AppendLine()
        End If
        desc.AppendLine("Click the links below to copy this base layout directly into your Clash of Clans game:")
        desc.AppendLine()
        desc.AppendLine($"🔗 **[PAK Layout Link (Slot 1)]({layoutData("link1")})**")

        If Not String.IsNullOrEmpty(layoutData("link2")) Then
            desc.AppendLine($"🔗 **[Alternative Link (Slot 2)]({layoutData("link2")})**")
        End If

        embed.WithDescription(desc.ToString())

        ' Attach Screenshot if available
        If Not String.IsNullOrEmpty(layoutData("image")) Then
            embed.WithImageUrl(layoutData("image"))
        End If

        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText("Clash of Clans Layout Service"))

        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function
    Public Async Function HandleBasesCommandAsync(command As SocketSlashCommand) As Task
        ' Verhindert den Discord-Timeout während des Datenbankzugriffs
        Await command.DeferAsync()

        ' 1. Rufe alle Layout-Datensätze aus der Oracle-DB ab
        Dim allLayouts As List(Of Dictionary(Of String, String)) = Await OracleDatabaseManager.GetAllLayoutsAsync()

        If allLayouts Is Nothing OrElse allLayouts.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ No layouts found inside the database.")
            Return
        End If

        ' Start des Embed-Aufbaus
        Dim embedBuilder As New EmbedBuilder() With {
        .Title = "🛡️ List of all FWA bases",
        .Color = Color.Purple,
        .Timestamp = DateTimeOffset.Now
    }

        Dim desc As New Text.StringBuilder()
        Dim quickCommandsList As New List(Of String)()

        ' 2. Durchlaufe die Liste rückwärts (oder sortiere sie passend von TH18 bis TH11)
        ' Wir ordnen die Einträge nach den Town Hall Stufen im Namen an
        For th As Integer = 18 To 9 Step -1
            Dim targetThName As String = $"TH{th}"

            ' Finde das passende Layout für die aktuelle TH-Stufe aus der Datenbank-Liste
            Dim currentLayout = allLayouts.FirstOrDefault(Function(l) l("name").ToUpper().Contains(targetThName))

            If currentLayout IsNot Nothing Then
                ' Überschrift der Base
                desc.AppendLine($"**{currentLayout("name").ToUpper()}**")

                ' Link 1 einbinden
                If Not String.IsNullOrEmpty(currentLayout("link1")) Then
                    desc.AppendLine($"[👉 Click here to copy]({currentLayout("link1")})")
                Else
                    desc.AppendLine("*[👉 Click here to copy (Link missing)]*")
                End If

                ' Optionalen Link 2 einbinden
                If Not String.IsNullOrEmpty(currentLayout("link2")) Then
                    desc.AppendLine($"[👉 Alternative Link]({currentLayout("link2")})")
                End If

                ' Falls Infos zum Layout in der DB hinterlegt sind, zeigen wir diese an
                If Not String.IsNullOrEmpty(currentLayout("info")) Then
                    desc.AppendLine($"📝 *{currentLayout("info")}*")
                End If

                desc.AppendLine() ' Leerzeile für sauberen Abstand

            End If
        Next
        ' Befehl für die Schnellauswahl zur Liste hinzufügen
        quickCommandsList.Add($"`/layout [Templatename]`")

        embedBuilder.WithDescription(desc.ToString())

        ' 3. Den Info-Block für die Detail-Befehle dynamisch generieren
        If quickCommandsList.Count > 0 Then
            ' Kehrt die Liste um, damit die Anzeige von links nach rechts mit ?th11 startet
            quickCommandsList.Reverse()
            Dim commandsString As String = String.Join(" - ", quickCommandsList)

            embedBuilder.AddField("ℹ️ Detailed Information",
            $"For detailed infos about our bases, type:{Environment.NewLine}{commandsString}")
        End If

        embedBuilder.WithFooter(footer:=New EmbedFooterBuilder().WithText("Clash of Clans Layout Service • MyCOCBot"))

        ' Das fertig zusammengestellte Embed an Discord senden
        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embedBuilder.Build())
    End Function

    Public Async Function HandleAutocompleteAsync(autocomplete As SocketAutocompleteInteraction) As Task
        Select Case autocomplete.Data.CommandName
            Case "cl", "cc"
                ' Get what the user has typed so far
                Dim userInput As String = autocomplete.Data.Options.First().Value.ToString().ToLower()

                ' Fetch all registered clans for this server from Oracle DB
                Dim clans = Await OracleDatabaseManager.GetClansAsync(autocomplete.GuildId.Value)

                ' Filter based on user typing (matches name or tag)
                Dim matches = clans.Where(Function(c) c.Item1.ToLower().Contains(userInput) OrElse c.Item2.ToLower().Contains(userInput)).Take(25)

                Dim results As New List(Of AutocompleteResult)()
                For Each clan In matches
                    ' Preview format in the dropdown list: "PK CWL 27 (#2JLJVYQPU)"
                    Dim previewText As String = $"{clan.Item2} ({clan.Item1})"
                    ' Value sent to the bot execution backend will be the clean tag
                    results.Add(New AutocompleteResult(previewText, clan.Item1))
                Next

                Await autocomplete.RespondAsync(results)
            Case "layout"
                Dim userInput As String = autocomplete.Data.Options.First().Value.ToString().ToLower()

                ' Fetch layouts from Oracle DB
                Dim layouts = Await OracleDatabaseManager.GetBaseLayoutsAsync(autocomplete.GuildId.Value)

                ' Filter based on what the user typed
                Dim matches = layouts.Where(Function(l) l.Item2.ToLower().Contains(userInput)).Take(25)

                Dim results As New List(Of AutocompleteResult)()
                For Each layout In matches
                    ' User sees the name, Bot receives the layout_id as String
                    results.Add(New AutocompleteResult(layout.Item2, layout.Item1.ToString()))
                Next

                Await autocomplete.RespondAsync(results)
            Case "whois"
                Await HandleWhoIsAutocompleteAsync(autocomplete)

        End Select
    End Function
    ' English Code Comments
    ''' <summary>
    ''' Intercepts and processes component interactions (like buttons) triggered in Discord channels.
    ''' </summary>
    Public Async Function HandleButtonExecutionAsync(component As SocketMessageComponent) As Task
        If component.Data.CustomId = "refresh_clan_list" Then
            Try
                Await component.DeferAsync()

                ' Invoke the identical central core engine to pull brand new live stats
                Dim messageData = Await BuildClanListMessageAsync(component.GuildId.Value)

                If messageData IsNot Nothing Then
                    ' Modify the existing UI frame with synchronized dataset changes
                    Await component.ModifyOriginalResponseAsync(Sub(p)
                                                                    p.Embed = messageData.Item1
                                                                    p.Components = messageData.Item2
                                                                End Sub)
                    API_COC.DebugPrint("[BUTTON SUCCESS] Clan list embed was synchronized perfectly via code re-use.")
                Else
                    Await component.ModifyOriginalResponseAsync(Sub(p)
                                                                    p.Content = "ℹ️ No clans are registered to this server database. Use `/clan-add` to begin."
                                                                    p.Embed = Nothing
                                                                    p.Components = Nothing
                                                                End Sub)
                End If
            Catch ex As Exception
                Console.WriteLine($"[BUTTON CRITICAL] Failed to execute interactive code re-use pipeline: {ex.Message}")
            End Try
        End If
    End Function
    ' =========================================================================
    ' SLASH COMMAND IMPLEMENTATIONS
    ' =========================================================================

    Private Async Function HandlePingCommandAsync(command As SocketSlashCommand) As Task
        ' -----------------------------------------------------------------
        ' COMMAND: /ping
        ' -----------------------------------------------------------------
        Const Version As String = "01.00.00 F"
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

    ''' <summary>
    ''' Securely broadcasts styled Discord news embeds by parsing placeholders and uploading
    ''' local Oracle VM image assets directly into the Discord Webhook infrastructure schema.
    ''' </summary>
    Private Async Function HandleNewsCommandAsync(command As SocketSlashCommand) As Task
        Dim gUser = TryCast(command.User, SocketGuildUser)
        If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

            ' 1. Instantly secure the critical 3-second Discord API interaction window (ephemeral feedback)
            Await command.DeferAsync(ephemeral:=True)

            ' Extract command parameters (channel and template file selection targets)
            Dim targetChannelOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "channel")
            Dim templateFileOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "templatefile")

            Dim errorMessage As String = ""
            Dim successMessage As String = ""
            Dim isCrashed As Boolean = False
            Dim imageName As String = "avatar1.png"
            Dim localAvatarPath As String = ""

            If targetChannelOption IsNot Nothing AndAlso templateFileOption IsNot Nothing Then
                Dim targetChannel As SocketTextChannel = TryCast(targetChannelOption.Value, SocketTextChannel)
                Dim targetFileName As String = templateFileOption.Value.ToString()

                ' Enforce correct file extension formatting structure layout
                If Not targetFileName.ToLower().EndsWith(".json") Then targetFileName &= ".json"

                If targetChannel IsNot Nothing Then
                    Dim renderedEmbed As Embed = Nothing
                    Dim templatePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates", targetFileName)

                    Try
                        ' 2. Intercept and parse raw file metadata strings to extract dynamic image properties
                        If Not File.Exists(templatePath) Then
                            Throw New FileNotFoundException($"Template path not found on application partition storage mapping layers.")
                        End If

                        Dim rawJson As String = File.ReadAllText(templatePath)
                        Dim jsonObject As JObject = JObject.Parse(rawJson)

                        ' Read the custom unique filename key directly out of the file body layout
                        imageName = If(jsonObject("filename")?.ToString(), "avatar1.png")

                        ' Compile targeted image path tracking locations pointing inside runtime environments
                        Dim baseDirectory As String = AppDomain.CurrentDomain.BaseDirectory
                        localAvatarPath = Path.Combine(baseDirectory, "images", imageName)

                        ' 3. Populate matching replacement variable collections
                        ' We clear out the old placeholder mapping since the image is handled natively at the webhook layer
                        Dim placeholders As New Dictionary(Of String, String) From {
                        {"{{USERNAME}}", command.User.Username},
                        {"{{USER_AVATAR}}", ""},
                        {"{{SERVER_NAME}}", targetChannel.Guild.Name},
                        {"{{CHANNEL_NAME}}", targetChannel.Name}
                    }

                        ' Execute compilation pipeline transformations across the custom structural configuration
                        renderedEmbed = EmbedEngine.Render(targetFileName, placeholders)

                    Catch ex As FileNotFoundException
                        errorMessage = $"[Configuration Error] Template file `{targetFileName}` not located inside application storage targets."
                        isCrashed = True
                    Catch ex As Exception
                        errorMessage = $"[Metadata Error] Extraction tracking pipeline failed: {ex.Message}"
                        isCrashed = True
                    End Try

                    ' 4. If metadata loading and rendering compiled cleanly, deploy downstream transport via Option 1
                    If Not isCrashed Then
                        Dim webhookClient As DiscordWebhookClient = Nothing
                        Dim tempWebhook As IWebhook = Nothing
                        Dim avatarStream As FileStream = Nothing

                        Try
                            ' Scan for matching active broadcast tunnels already bound to target
                            Dim existingWebhooks = Await targetChannel.GetWebhooksAsync()
                            tempWebhook = existingWebhooks.FirstOrDefault(Function(w) w.Name = "Pak News Webhook")

                            ' If no webhook exists, create it freshly while injecting the raw VM image file binary layout
                            ' Wenn kein Webhook existiert, erstellen wir einen neuen
                            ' Wenn kein Webhook existiert, erstellen wir einen neuen
                            If tempWebhook Is Nothing Then
                                ' Prüfung: Existiert das Bild lokal auf der Oracle VM?
                                If File.Exists(localAvatarPath) Then
                                    Try
                                        ' Öffne die lokale Bilddatei auf der VM als Stream
                                        avatarStream = New FileStream(localAvatarPath, FileMode.Open, FileAccess.Read)

                                        ' FIX BC30311: Übergreife den Stream DIREKT an die Methode.
                                        ' CreateWebhookAsync erwartet nativ einen Stream für das Avatar-Bild!
                                        tempWebhook = Await targetChannel.CreateWebhookAsync("Pak News Webhook", avatarStream)
                                    Catch imageEx As Exception
                                        API_COC.DebugPrint($"[Webhook Image Error] Failed to apply local VM avatar: {imageEx.Message}")
                                        ' Fallback: Webhook ohne Bild erstellen, falls Datei beschädigt ist
                                    End Try
                                    If tempWebhook Is Nothing Then
                                        tempWebhook = Await targetChannel.CreateWebhookAsync("Pak News Webhook")

                                    End If

                                Else
                                    ' Fallback: Wenn Datei auf der VM fehlt
                                    tempWebhook = Await targetChannel.CreateWebhookAsync("Pak News Webhook")
                                End If
                            End If
                            ' Bind clean client processing interface engine
                            webhookClient = New DiscordWebhookClient(tempWebhook.Id, tempWebhook.Token)

                            ' Transmit mapped metrics elements via core endpoint channels. 
                            ' We omit the avatarUrl parameter completely because the profile icon is natively saved inside the webhook shell.
                            Await webhookClient.SendMessageAsync(
                            text:="",
                            embeds:={renderedEmbed},
                            username:="Pak Admin News System"
                        )

                            successMessage = $"🚀 Successfully posted news template `{targetFileName}` into <#{targetChannel.Id}> using uploaded Oracle VM avatar!"

                        Catch ex As Discord.Net.HttpException When ex.DiscordCode = DiscordErrorCode.InsufficientPermissions
                            errorMessage = "[Permissions Error] Bot infrastructure missing explicit 'Manage Webhooks' privilege level."
                            isCrashed = True
                        Catch ex As Exception
                            errorMessage = $"[Transport Error] Remote webhook streaming transmission dropped: {ex.Message}"
                            isCrashed = True
                        Finally
                            ' Resource Management: Safely close active file streams and webhook clients to prevent memory leaks
                            If avatarStream IsNot Nothing Then
                                avatarStream.Dispose()
                            End If
                            If webhookClient IsNot Nothing Then
                                webhookClient.Dispose()
                            End If
                        End Try

                        ' Clean up tracking webhooks AFTER complete payload delivery to avoid thread blocking loops
                        If tempWebhook IsNot Nothing Then
                            Try
                                Await tempWebhook.DeleteAsync()
                            Catch ex As Exception
                                API_COC.DebugPrint($"[Webhook Clean Warning] Failed deleting infrastructure segment safely: {ex.Message}")
                            End Try
                        End If
                    End If
                Else
                    errorMessage = "[Input Error] Selected target channel is invalid or configuration parameters failed mapping arrays."
                    isCrashed = True
                End If
            Else
                errorMessage = "[Input Error] Missing critical argument configuration requirements."
                isCrashed = True
            End If

            ' Dispatch responses cleanly outside protected try-catch wrappers to avoid BC36943 exceptions
            If isCrashed Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = errorMessage)
            Else
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = successMessage)
            End If
        Else
            ' Fallback interaction block dropped immediately if role parameters are missing
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
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

                embedBuilder.WithFooter("`Pak Admin Bot System`")
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

            embedBuilder.WithFooter("`Pak Admin Bot System`")

            Await command.RespondAsync(embed:=embedBuilder.Build(), ephemeral:=True)
        Else
            Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
        End If

    End Function

    Private Async Function HandleChannelsCommandAsync(command As SocketSlashCommand) As Task
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

    Public Async Function GetResourceUsageEmbedAsync(client As DiscordSocketClient) As Task(Of EmbedBuilder)
        ' 1. Gather Bot Metrics
        Dim currentProcess As Process = Process.GetCurrentProcess()
        Dim botRamUsageMB As Double = currentProcess.WorkingSet64 / (1024.0 * 1024.0)

        Dim uptime As TimeSpan = DateTime.Now - currentProcess.StartTime
        Dim uptimeString As String = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
        Dim osInfo As String = RuntimeInformation.OSDescription
        Dim ping As Integer = client.Latency

        ' 2. Gather Database Metrics Asynchronously
        Dim dbStats As Dictionary(Of String, String) = Await OracleDatabaseManager.GetDatabaseStatsAsync()

        ' 3. Build UI Layout
        Dim embed As New EmbedBuilder() With {
            .Title = "📊 Bot, Server & DB Resource Status",
            .Color = Color.Green,
            .Timestamp = DateTimeOffset.Now
        }

        embed.AddField("🤖 Bot Performance",
                       $"**Uptime:** {uptimeString}{Environment.NewLine}" &
                       $"**RAM Used:** {botRamUsageMB:F2} MB{Environment.NewLine}" &
                       $"**Gateway Ping:** {ping} ms", inline:=True)

        embed.AddField("🗄️ Oracle DB Stats",
                       $"**Data Size:** {dbStats("SizeMB")}{Environment.NewLine}" &
                       $"**Active Sessions:** {dbStats("Sessions")}{Environment.NewLine}" &
                       $"**Free Tier Cap:** 20 GB", inline:=True)

        embed.AddField("☁️ Host Environment",
                       $"**OS:** {osInfo}{Environment.NewLine}" &
                       $"**Arch:** {RuntimeInformation.OSArchitecture}{Environment.NewLine}" &
                       $".NET version: {RuntimeInformation.FrameworkDescription}", inline:=False)

        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText("Provided by `Pak Admin Bot System`"))

        Return embed
    End Function
    Private Async Function HandleWhoIsAutocompleteAsync(interaction As SocketAutocompleteInteraction) As Task
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
    ''' Converts fancy mathematical fonts and surrogate-pair unicode glyphs into raw readable ASCII text.
    ''' Crucial for Linux VMs running on Oracle OCI to match strings correctly.
    ''' </summary>
    Private Function DeUnicodeString(input As String) As String
        If String.IsNullOrEmpty(input) Then Return ""

        Dim sb As New System.Text.StringBuilder()
        Dim i As Integer = 0

        While i < input.Length
            ' Check if this character is part of a 4-Byte Surrogate Pair (like mathematical bold text 𝗧𝗼𝗺)
            If Char.IsHighSurrogate(input(i)) AndAlso (i + 1) < input.Length AndAlso Char.IsLowSurrogate(input(i + 1)) Then
                ' Convert the two 16-bit characters into a single 32-bit UTF-32 Code Point
                Dim codePoint As Integer = Char.ConvertToUtf32(input(i), input(i + 1))

                ' 1. Check for Mathematical Bold Capital Letters (𝗧)
                If codePoint >= &H1D5D4 AndAlso codePoint <= &H1D5ED Then
                    sb.Append(Convert.ToChar(codePoint - &H1D5D4 + 65)) ' Map to A-Z
                    ' 2. Check for Mathematical Bold Small Letters (𝗼, 𝗺)
                ElseIf codePoint >= &H1D5EE AndAlso codePoint <= &H1D607 Then
                    sb.Append(Convert.ToChar(codePoint - &H1D5EE + 97)) ' Map to a-z
                Else
                    ' If it's another special character, keep it as is
                    sb.Append(input(i))
                    sb.Append(input(i + 1))
                End If
                i += 2 ' Skip both surrogate characters
            Else
                ' Standard 2-Byte ASCII/Unicode character
                sb.Append(input(i))
                i += 1
            End If
        End While

        Return sb.ToString()
    End Function
End Class
