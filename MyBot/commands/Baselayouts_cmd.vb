Imports Discord
Imports Discord.WebSocket

Public Module BaselayoutsCommands
    Public Class Baselayouts

        Public Shared Async Function RegisterBaseLayoutCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
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


        End Function

        '' <summary>
        ''' Processes the creation of a new base layout entry via /layout-add
        ''' </summary>
        Public Shared Async Function HandleLayoutAddAsync(command As SocketSlashCommand) As Task
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
        Public Shared Async Function HandleLayoutCommandAsync(command As SocketSlashCommand) As Task
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
        Public Shared Async Function HandleBasesCommandAsync(command As SocketSlashCommand) As Task
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


        Public Shared Async Function HandleBaseLayoutAsync(autocomplete As SocketAutocompleteInteraction) As Task
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

        End Function

    End Class
End Module
