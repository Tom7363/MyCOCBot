Imports System.IO
Imports System.Text.Json
Imports Discord

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