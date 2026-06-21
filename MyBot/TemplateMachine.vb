Imports System.IO
Imports System.Text.Json
Imports Discord
Imports Discord.Webhook
Imports Discord.WebSocket
Imports Newtonsoft.Json.Linq
Public Class TemplateCommands
    Public Shared Async Function RegisterTemplateCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
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

        ' Füge diese Zeile zu den anderen Creates hinzu
        Await guild.CreateApplicationCommandAsync(newsCmd.Build())

        ' Send to Discord API asynchronously
        Await guild.CreateApplicationCommandAsync(templCmd.Build())

    End Function

    Public Shared Async Function HandleTemplateCommandAsync(command As SocketSlashCommand) As Task
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
    Public Shared Async Function HandleNewsCommandAsync(command As SocketSlashCommand) As Task
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


End Class
Public Class EmbedEngine
    ' =========================================================================
    ' VARIANTE 1: Datenstrukturen für das einfache flache Custom-Format
    ' =========================================================================
    Public Class CustomTemplate
        Public Property Title As String
        Public Property Description As String
        Public Property Color As String
        Public Property ThumbnailUrl As String
        Public Property Fields As List(Of CustomFieldTemplate)
        Public Property FooterText As String
    End Class

    Public Class CustomFieldTemplate
        Public Property Name As String
        Public Property Value As String
        Public Property IsInline As Boolean
    End Class

    ' =========================================================================
    ' VARIANTE 2: Datenstrukturen für das offizielle Discord Webhook-Format
    ' =========================================================================
    Public Class WebhookRoot
        Public Property Content As String
        Public Property Embeds As List(Of WebhookEmbed)
    End Class

    Public Class WebhookEmbed
        Public Property Title As String
        Public Property Description As String
        Public Property Color As Object
        Public Property Thumbnail As WebhookUrl
        Public Property Fields As List(Of WebhookField)
        Public Property Footer As WebhookFooter
    End Class

    Public Class WebhookUrl
        Public Property Url As String
    End Class

    Public Class WebhookField
        Public Property Name As String
        Public Property Value As String
        Public Property Inline As Boolean
    End Class

    Public Class WebhookFooter
        Public Property Text As String
    End Class

    ''' <summary>
    ''' Erkennt das JSON-Format automatisch, wendet Variablen an und gibt ein fertiges Embed aus.
    ''' </summary>
    Public Shared Function Render(fileName As String, replacements As Dictionary(Of String, String)) As Embed
        ' 1. Pfad auflösen und prüfen
        ' 1. Pfad auflösen – Jetzt inklusive dem Unterordner "templates"
        Dim templatePath As String = Path.Combine(AppContext.BaseDirectory, "templates", fileName)

        If Not File.Exists(templatePath) Then
            Throw New FileNotFoundException($"Template layout file was not found inside the templates subfolder.", templatePath)
        End If

        ' 2. JSON-Inhalt einlesen
        Dim jsonContent As String = File.ReadAllText(templatePath)

        ' 3. Process replacements recursively based on passed Key/Value pairs
        For Each kvp In replacements
            Dim safeValue As String = kvp.Value

            If Not String.IsNullOrEmpty(safeValue) Then
                ' 1. Fix the core Discord.Net newline representation (\n) first
                safeValue = safeValue.Replace("\n", vbCrLf)

                ' 2. Escape backslashes globally to secure JSON structural safety
                safeValue = safeValue.Replace("\", "\\")

                ' 3. Escape double quotes (Crucial for: "THE ORIGINALS")
                safeValue = safeValue.Replace("""", "\""")

                ' 4. Translate all structural line feeds into standardized JSON tokens
                safeValue = safeValue.Replace(vbCrLf, "\n").Replace(vbLf, "\n").Replace(vbCr, "\n")

                ' 5. SAFETY CHECKS: Ensure the string does not end on an unescaped tracking slash
                ' If it ends with an uneven number of sashes, it will break the enclosing JSON quote.
                If safeValue.EndsWith("\") AndAlso Not safeValue.EndsWith("\\") Then
                    safeValue &= " " ' Append a safe spacing character to prevent escaping the JSON token quote
                End If
            End If

            jsonContent = jsonContent.Replace(kvp.Key, safeValue)
        Next

        ' 4. JSON-Struktur analysieren, um das Format zu bestimmen
        Dim options As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}

        Using doc As JsonDocument = JsonDocument.Parse(jsonContent)
            Dim rootElement As JsonElement = doc.RootElement

            ' Wenn ein "embeds"-Property existiert, handelt es sich um das Webhook-Format
            If rootElement.TryGetProperty("embeds", Nothing) OrElse rootElement.TryGetProperty("Embeds", Nothing) Then

                ' PARSEN ALS WEBHOOK-FORMAT
                Dim webhookData As WebhookRoot = JsonSerializer.Deserialize(Of WebhookRoot)(jsonContent, options)

                If webhookData IsNot Nothing AndAlso webhookData.Embeds IsNot Nothing AndAlso webhookData.Embeds.Count > 0 Then
                    Dim wEmbed = webhookData.Embeds(0)
                    Dim builder As New EmbedBuilder()

                    If Not String.IsNullOrEmpty(wEmbed.Title) Then builder.WithTitle(wEmbed.Title)
                    If Not String.IsNullOrEmpty(wEmbed.Description) Then builder.WithDescription(wEmbed.Description)

                    If wEmbed.Color IsNot Nothing Then
                        Dim rawColor As UInteger
                        If UInteger.TryParse(wEmbed.Color.ToString(), rawColor) Then builder.WithColor(New Color(rawColor))
                    End If

                    If wEmbed.Thumbnail IsNot Nothing AndAlso Not String.IsNullOrEmpty(wEmbed.Thumbnail.Url) Then
                        builder.WithThumbnailUrl(wEmbed.Thumbnail.Url)
                    End If

                    If wEmbed.Fields IsNot Nothing Then
                        For Each f In wEmbed.Fields
                            builder.AddField(f.Name, f.Value, f.Inline)
                        Next
                    End If

                    If wEmbed.Footer IsNot Nothing AndAlso Not String.IsNullOrEmpty(wEmbed.Footer.Text) Then
                        builder.WithFooter(Sub(footer) footer.Text = wEmbed.Footer.Text)
                    End If

                    Return builder.Build()
                End If
            Else

                ' PARSEN ALS FLACHES CUSTOM-FORMAT
                Dim customData As CustomTemplate = JsonSerializer.Deserialize(Of CustomTemplate)(jsonContent, options)

                If customData IsNot Nothing Then
                    Dim builder As New EmbedBuilder()

                    If Not String.IsNullOrEmpty(customData.Title) Then builder.WithTitle(customData.Title)
                    If Not String.IsNullOrEmpty(customData.Description) Then builder.WithDescription(customData.Description)

                    Dim rawColor As UInteger
                    If UInteger.TryParse(customData.Color, rawColor) Then builder.WithColor(New Color(rawColor))

                    If Not String.IsNullOrEmpty(customData.ThumbnailUrl) Then builder.WithThumbnailUrl(customData.ThumbnailUrl)

                    If customData.Fields IsNot Nothing Then
                        For Each f In customData.Fields
                            builder.AddField(f.Name, f.Value, f.IsInline)
                        Next
                    End If

                    If Not String.IsNullOrEmpty(customData.FooterText) Then
                        builder.WithFooter(Sub(footer) footer.Text = customData.FooterText)
                    End If

                    Return builder.Build()
                End If
            End If
        End Using

        Throw New InvalidDataException("The JSON layout matches neither the Custom layout nor the Discord Webhook format.")
    End Function
End Class