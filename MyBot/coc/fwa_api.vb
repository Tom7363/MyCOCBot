Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports Discord
Imports Discord.Rest
Imports Discord.WebSocket
Imports HtmlAgilityPack
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client

Module FWA_API
    Public Class FWAStats_API
        Public Shared Async Function RegisterFWAStatsAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            ' =========================================================================
            ' NEW COMMAND: /weight-update
            ' =========================================================================
            Dim weightUpdateCmd = New SlashCommandBuilder() With {
    .Name = "weight-update",
    .Description = "Downloads live FWA weights and updates or creates the Oracle database table"
}

            Await guild.CreateApplicationCommandAsync(weightUpdateCmd.Build())

        End Function

        ''' <summary>
        ''' Fetches all members of FWA clans and dynamically creates a custom-named table.
        ''' </summary>
        Public Shared Async Function HandleWeigthUpdateAsync(command As SocketSlashCommand) As Task
            ' FIX 1: Instantly acknowledge the interaction to secure a 15-minute processing window
            Await command.DeferAsync()

            ' Track crash states safely outside the catch block scope
            Dim isCrashed As Boolean = False
            Dim errorText As String = ""

            Try
                ' Push the initial visual update using the tokenized interaction hook
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "⏳ **Step 1/3:** Dropping and recreating relational `WEIGHTS` schema...")

                ' 1. Recreate table structure
                Await RecreateWeightsTableAsync()
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "⏳ **Step 2/3:** Structural reset complete. Opening streaming pipeline to fwastats.com...")

                ' 2. Stream dataset over network
                Dim freshRecords As List(Of FwaRecord) = Await GetPlayerWeightsFromWeb()

                If freshRecords IsNot Nothing AndAlso freshRecords.Count > 0 Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"⏳ **Step 3/3:** Successfully decoded {freshRecords.Count} entries. Performing Oracle Bulk-Insert...")

                    ' 3. Bulk insert into Oracle AI Database
                    Await SaveFwaDataAsync(freshRecords)

                    ' Final Success Receipt Output
                    Dim finalSummary As String = $"✅ **Weight Update Complete!**" & vbCrLf &
                                             $"📋 Relational Destination Table: `WEIGHTS`" & vbCrLf &
                                             $"👥 Total Cached Elements: `{freshRecords.Count}`"

                    ' FIX 2: Modify the original token response instead of calling a new separate channel print action
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = finalSummary)
                    API_COC.DebugPrint("[COMMAND SUCCESS] /weight-update completed execution cycle successfully.")
                Else
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ **Pipeline Failed:** Zero data records fetched over the network channel.")
                End If

            Catch ex As Exception
                isCrashed = True
                errorText = ex.Message
                API_COC.DebugPrint("[COMMAND EXCEPTION] Runtime failure during execution: " & errorText)
            End Try

            ' Safe post-exception routing block bypassing structural compiler rules
            If isCrashed Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ **Critical Error:** Update routine crashed!" & vbCrLf & $"`{errorText}`")
            End If

            ' 1. Sichert das 3-Sekunden-Zeitfenster auf deiner Oracle Linux VM
            Await command.DeferAsync()
            API_COC.DebugPrint("[COMMAND] Execution triggered via /weight-update.")

            ' 2. Nutze die Roster-Initialisierungsmethode direkt für das manuelle Update
            Await OracleDatabaseManager.RecreateWeightsTableAsync()
            Await SaveFwaDataAsync(Await GetPlayerWeightsFromWeb())
            ' 3. Prüfung und Antwort an den Discord-Kanal senden

            API_COC.DebugPrint($"[COMMAND SUCCESS] /weight-update completed.")
            Await command.RespondAsync($"[COMMAND SUCCESS] /weight-update completed.", ephemeral:=True)

        End Function

    End Class
    Public Class ChocolateClashAPI
        ' Speicher für die Cloudflare-Sitzungsmerkmale (Gültig für ca. 1-2 Stunden)
        Private Shared _cachedCookies As String = String.Empty
        Private Shared _cachedUserAgent As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        Private Shared _cookieTimestamp As DateTime = DateTime.MinValue
        Private Shared ReadOnly _handler As New HttpClientHandler() With {
    .AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate Or DecompressionMethods.Brotli
}

        ' 1. HttpClient ganz normal ohne Inline-Header deklarieren
        Private Shared ReadOnly _httpClient As New HttpClient(_handler)

        ' 2. Statischer Konstruktor fügt die Header sicher und regelkonform hinzu
        Shared Sub New()
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=3600")
            _httpClient.DefaultRequestHeaders.Accept.Add(New System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"))
        End Sub
        Public Shared Property FlareSolverrSessionId As String = "fwa_bot_session"
        Private Shared ReadOnly _flaresolverrUrl As String = "http://localhost:8191/v1"

        Public Shared Async Function DestroyFlareSolverrSessionAsync() As Task
            Try
                Dim destroyPayload = New With {
            .cmd = "sessions.destroy",
            .session = FlareSolverrSessionId
        }
                Dim jsonDestroy As String = Newtonsoft.Json.JsonConvert.SerializeObject(destroyPayload)

                ' FIX: Wir nutzen hier eine lokale CancellationTokenSource mit maximal 5 Sekunden!
                ' Wenn FlareSolverr hängt, bricht diese Anfrage sofort ab, statt 60 Sekunden zu blockieren.
                Using shortTimeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(5))
                    Using request As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                        request.Content = New StringContent(jsonDestroy, Encoding.UTF8, "application/json")

                        Using response As HttpResponseMessage = Await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, shortTimeoutCts.Token)
                            API_COC.DebugPrint($"[SESSIONS] Closed background session '{FlareSolverrSessionId}' to release RAM allocation.")
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                ' Non-critical fail safe logging - verhindert das Hängenbleiben der gesamten VM-Pipeline
                API_COC.DebugPrint($"[SESSIONS WARNING] Session cleanup bypassed/timed out: {ex.Message}")
            End Try
        End Function
        Public Shared Async Function GetWebpageFromFlareSolver3(targetUrl As String) As Task(Of String)
            Dim maxRetryAttempts As Integer = 3
            Dim baseDelayMilliseconds As Integer = 2000
            Dim operationTimeoutSeconds As Integer = 45

            ' HINWEIS: Await EnsureFlareSolverrSessionAsync() WURDE ENTFERNT.
            ' Wir überlassen FlareSolverr die autonome Erstellung temporärer Browser-Instanzen.

            ' Das JSON-Objekt enthält KEINE .session Eigenschaft mehr!
            Dim payloadObj2 = New With {
        .cmd = "request.get",
        .url = targetUrl,
        .maxTimeout = 30000
    }
            ' Aggressives Payload: Weist FlareSolverr an, die Wartezeit nach dem Lösen zu streichen
            Dim payloadObj = New With {
        .cmd = "request.get",
        .url = targetUrl,
        .maxTimeout = 15000, ' Hard-Limit für FlareSolverr auf 15 Sekunden senken
        .wait = 0            ' 0 Millisekunden zusätzliche JavaScript-Wartezeit!
    }
            Dim jsonPayload As String = Newtonsoft.Json.JsonConvert.SerializeObject(payloadObj)

            ' Auto-Retry Schleife
            For attempt As Integer = 1 To maxRetryAttempts
                Using timeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(operationTimeoutSeconds))
                    Try
                        API_COC.DebugPrint($"[FLARESOLVERR] Requesting URL: '{targetUrl}' (Attempt {attempt}/{maxRetryAttempts})...")

                        Using request As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                            request.Content = New StringContent(jsonPayload, Encoding.UTF8, "application/json")

                            Using response As HttpResponseMessage = Await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                                response.EnsureSuccessStatusCode()

                                Dim proxyResponseString As String = Await response.Content.ReadAsStringAsync(timeoutCts.Token)
                                Dim jsonDoc As JObject = JObject.Parse(proxyResponseString)

                                ' FlareSolverr Status-Prüfung
                                Dim statusMsg As String = jsonDoc("status")?.ToString()
                                If statusMsg <> "ok" Then
                                    Dim errorMsg As String = jsonDoc("message")?.ToString()
                                    Throw New Exception($"FlareSolverr API responded with status error: {errorMsg}")
                                End If

                                ' Rohes HTML extrahieren
                                Dim rawHtmlContent As String = jsonDoc("solution")?("response")?.ToString()

                                ' Cloudflare-Warteschleife abfangen
                                If String.IsNullOrEmpty(rawHtmlContent) OrElse rawHtmlContent.Contains("Just a moment...") Then
                                    Throw New Exception("Cloudflare Challenge bypass failed (received block page).")
                                End If

                                API_COC.DebugPrint($"[FLARESOLVERR SUCCESS] Successfully fetched HTML ({rawHtmlContent.Length} characters).")
                                Return rawHtmlContent ' Erfolgreicher Rückgabewert, bricht die Schleife ab
                            End Using
                        End Using

                    Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse timeoutCts.IsCancellationRequested
                        API_COC.DebugPrint($"[FLARESOLVERR TIMEOUT] Attempt {attempt} exceeded processing deadline.")
                    Catch ex As Exception
                        API_COC.DebugPrint($"[FLARESOLVERR WARNING] Attempt {attempt} failed via engine error: {ex.Message}")
                    End Try
                End Using

                ' HINWEIS: Das fehleranfällige forceRecreate/Destroy-Session Management wurde komplett gelöscht!

                ' Exponential Backoff Delay (Wartezeit vor dem nächsten Versuch)
                If attempt < maxRetryAttempts Then
                    Dim currentDelay As Integer = baseDelayMilliseconds * CInt(Math.Pow(2, attempt - 1))
                    API_COC.DebugPrint($"[FLARESOLVERR RETRY] Sleeping for {currentDelay}ms before next engine attempt...")
                    Await Task.Delay(currentDelay)
                End If
            Next

            Throw New Exception($"[FLARESOLVERR CRITICAL] Exhausted all {maxRetryAttempts} attempts without retrieving target web content.")
        End Function
        Public Shared Async Function GetWebpageFromFlareSolver2(targetUrl As String) As Task(Of String)
            Dim maxRetryAttempts As Integer = 3
            Dim baseDelayMilliseconds As Integer = 2000
            Dim operationTimeoutSeconds As Integer = 45

            ' Cookies nach 60 Minuten vorsorglich verwerfen, um abgelaufene Challenges zu verhindern
            If DateTime.Now.Subtract(_cookieTimestamp).TotalMinutes > 60 Then
                _cachedCookies = String.Empty
            End If

            ' Dynamisches JSON-Payload aufbauen (Korrektur: Punkte vor den Feldern entfernt)
            Dim payloadObj As Object
            If Not String.IsNullOrEmpty(_cachedCookies) Then
                API_COC.DebugPrint("[FLARESOLVERR] Reusing cached session cookies for instant bypass...")
                payloadObj = New With {
            Key .cmd = "request.get",
            Key .url = targetUrl,
            Key .maxTimeout = 15000,
            Key .wait = 0,
            Key .cookies = Newtonsoft.Json.JsonConvert.DeserializeObject(Of JArray)(_cachedCookies)
        }
            Else
                API_COC.DebugPrint("[FLARESOLVERR] No valid cookies found. Engaging full 12s hardware solve...")
                payloadObj = New With {
            Key .cmd = "request.get",
            Key .url = targetUrl,
            Key .maxTimeout = 30000,
            Key .wait = 0
        }
            End If
            Dim jsonPayload As String = Newtonsoft.Json.JsonConvert.SerializeObject(payloadObj)

            For attempt As Integer = 1 To maxRetryAttempts
                Using timeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(operationTimeoutSeconds))
                    Try
                        Using request As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                            request.Content = New StringContent(jsonPayload, Encoding.UTF8, "application/json")

                            Using response As HttpResponseMessage = Await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                                response.EnsureSuccessStatusCode()

                                Dim proxyResponseString As String = Await response.Content.ReadAsStringAsync(timeoutCts.Token)
                                Dim jsonDoc As JObject = JObject.Parse(proxyResponseString)

                                If jsonDoc("status")?.ToString() <> "ok" Then
                                    Throw New Exception(jsonDoc("message")?.ToString())
                                End If

                                ' Wenn der Aufruf mit unseren gecachten Cookies fehlgeschlagen ist (Cookies abgelaufen)
                                Dim rawHtmlContent As String = jsonDoc("solution")?("response")?.ToString()
                                If String.IsNullOrEmpty(rawHtmlContent) OrElse rawHtmlContent.Contains("Just a moment...") Then
                                    If Not String.IsNullOrEmpty(_cachedCookies) Then
                                        API_COC.DebugPrint("[FLARESOLVERR] Cached cookies expired! Clearing cache and retrying...")
                                        _cachedCookies = String.Empty ' Cache löschen und in den nächsten Versuch zwingen
                                        Throw New Exception("Cookies invalid.")
                                    End If
                                    Throw New Exception("Challenge solve failed.")
                                End If

                                ' EXTRAKTION: Die frisch gelösten Cookies für die nächsten Abfragen sichern!
                                Dim responseCookies As JArray = CType(jsonDoc("solution")?("cookies"), JArray)
                                If responseCookies IsNot Nothing AndAlso responseCookies.Count > 0 Then
                                    _cachedCookies = responseCookies.ToString()
                                    _cookieTimestamp = DateTime.Now
                                End If

                                API_COC.DebugPrint($"[FLARESOLVERR SUCCESS] Fetched HTML in {attempt}. Versuch.")
                                Return rawHtmlContent
                            End Using
                        End Using

                    Catch ex As Exception
                        API_COC.DebugPrint($"[FLARESOLVERR WARNING] Attempt {attempt} temporary fail: {ex.Message}")
                        ' Falls die Cookies abgelaufen waren, leeren wir sie für den nächsten Schleifendurchlauf sofort
                        _cachedCookies = String.Empty
                    End Try
                End Using

                If attempt < maxRetryAttempts Then
                    Await Task.Delay(baseDelayMilliseconds)
                End If
            Next

            Throw New Exception("[FLARESOLVERR CRITICAL] All cookie-hybrid execution lanes exhausted.")
        End Function
        Public Shared Async Function EnsureFlareSolverrSessionAsync(Optional forceRecreate As Boolean = False) As Task
            Try
                ' Wenn ein Neuaufbau erzwungen wird, löschen wir die Session mit dem kurzen Timeout
                If forceRecreate Then
                    API_COC.DebugPrint($"[SESSIONS] Enforcing recycling of session '{FlareSolverrSessionId}' due to previous 500 error...")
                    Await DestroyFlareSolverrSessionAsync()
                End If

                ' 1. Aktive Sessions abrufen (Ebenfalls mit lokalem Schutz-Timeout von 5 Sekunden)
                Dim listPayload = New With {.cmd = "sessions.list"}
                Dim jsonList As String = Newtonsoft.Json.JsonConvert.SerializeObject(listPayload)

                Using mediumTimeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(25))
                    Using requestList As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                        requestList.Content = New StringContent(jsonList, Encoding.UTF8, "application/json")

                        Using responseList As HttpResponseMessage = Await _httpClient.SendAsync(requestList, HttpCompletionOption.ResponseHeadersRead, mediumTimeoutCts.Token)
                            responseList.EnsureSuccessStatusCode()

                            Dim responseString As String = Await responseList.Content.ReadAsStringAsync(mediumTimeoutCts.Token)
                            Dim jsonDoc As JObject = JObject.Parse(responseString)
                            Dim sessionsArray As JArray = CType(jsonDoc("sessions"), JArray)

                            If sessionsArray IsNot Nothing AndAlso Not forceRecreate Then
                                Dim activeSessionIds As List(Of String) = sessionsArray.Select(Function(t) t.ToString()).ToList()

                                If activeSessionIds.Contains(FlareSolverrSessionId) Then
                                    API_COC.DebugPrint($"[SESSIONS] Persistent session '{FlareSolverrSessionId}' is warm and active.")
                                    Return
                                End If
                            End If
                        End Using
                    End Using
                End Using

                ' 2. Neue Session erstellen (Hier erlauben wir bis zu 15 Sekunden, da Chromium auf der VM starten muss)
                API_COC.DebugPrint($"[SESSIONS] Creating fresh persistent session: {FlareSolverrSessionId}...")
                ' 2. Neue Session erstellen (Force a real Desktop Chromium Signature)
                API_COC.DebugPrint($"[SESSIONS] Creating fresh persistent session: {FlareSolverrSessionId}...")

                Dim createPayload = New With {
    .cmd = "sessions.create",
    .session = FlareSolverrSessionId,
    .userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
}

                Dim jsonCreate As String = Newtonsoft.Json.JsonConvert.SerializeObject(createPayload)

                Using mediumTimeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(15))
                    Using requestCreate As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                        requestCreate.Content = New StringContent(jsonCreate, Encoding.UTF8, "application/json")

                        Using responseCreate As HttpResponseMessage = Await _httpClient.SendAsync(requestCreate, HttpCompletionOption.ResponseHeadersRead, mediumTimeoutCts.Token)
                            responseCreate.EnsureSuccessStatusCode()
                            API_COC.DebugPrint($"[SESSIONS SUCCESS] Fresh session '{FlareSolverrSessionId}' successfully spawned.")
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                API_COC.DebugPrint($"[SESSIONS CRITICAL] Failed to manage persistent background session: {ex.Message}")
            End Try
        End Function
        Public Shared Async Function GetWebpageFromFlareSolver(targetUrl As String) As Task(Of String)
            Dim maxRetryAttempts As Integer = 3
            Dim baseDelayMilliseconds As Integer = 2000
            Dim operationTimeoutSeconds As Integer = 45

            ' Sicherstellen, dass die persistente Session warm und aktiv ist
            Await EnsureFlareSolverrSessionAsync()

            Dim payloadObj = New With {
        .cmd = "request.get",
        .session = FlareSolverrSessionId,
        .url = targetUrl,
        .maxTimeout = 30000
    }
            Dim jsonPayload As String = Newtonsoft.Json.JsonConvert.SerializeObject(payloadObj)

            ' Auto-Retry Schleife
            For attempt As Integer = 1 To maxRetryAttempts
                Dim recreateSessionRequired As Boolean = False

                Using timeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(operationTimeoutSeconds))
                    Try
                        API_COC.DebugPrint($"[FLARESOLVERR] Requesting URL: '{targetUrl}' (Attempt {attempt}/{maxRetryAttempts})...")

                        Using request As New HttpRequestMessage(HttpMethod.Post, _flaresolverrUrl)
                            request.Content = New StringContent(jsonPayload, Encoding.UTF8, "application/json")

                            Using response As HttpResponseMessage = Await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                                response.EnsureSuccessStatusCode()

                                Dim proxyResponseString As String = Await response.Content.ReadAsStringAsync(timeoutCts.Token)
                                Dim jsonDoc As JObject = JObject.Parse(proxyResponseString)

                                ' FlareSolverr Status-Prüfung
                                Dim statusMsg As String = jsonDoc("status")?.ToString()
                                If statusMsg <> "ok" Then
                                    Dim errorMsg As String = jsonDoc("message")?.ToString()
                                    Throw New Exception($"FlareSolverr API responded with status error: {errorMsg}")
                                End If

                                ' Rohes HTML extrahieren
                                Dim rawHtmlContent As String = jsonDoc("solution")?("response")?.ToString()

                                ' Cloudflare-Warteschleife abfangen
                                If String.IsNullOrEmpty(rawHtmlContent) OrElse rawHtmlContent.Contains("Just a moment...") Then
                                    Throw New Exception("Cloudflare Challenge bypass failed (received block/challenge page).")
                                End If

                                API_COC.DebugPrint($"[FLARESOLVERR SUCCESS] Successfully fetched HTML ({rawHtmlContent.Length} characters).")
                                Return rawHtmlContent ' Erfolgreicher Rückgabewert
                            End Using
                        End Using

                    Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse timeoutCts.IsCancellationRequested
                        API_COC.DebugPrint($"[FLARESOLVERR TIMEOUT] Attempt {attempt} exceeded processing deadline.")
                        recreateSessionRequired = True
                    Catch ex As Exception
                        API_COC.DebugPrint($"[FLARESOLVERR WARNING] Attempt {attempt} failed: {ex.Message}")

                        ' Safe State-Check außerhalb des Catch-Blocks (Kompatibel mit älteren VB.NET Versionen)
                        If ex.ToString().Contains("500") OrElse ex.ToString().Contains("StatusCode") Then
                            API_COC.DebugPrint("[FLARESOLVERR] Internal 500 error detected. Flagging session for automatic recreation...")
                            recreateSessionRequired = True
                        End If
                    End Try
                End Using

                ' Session-Recycling sicher außerhalb des Catch-Blocks ausführen
                If recreateSessionRequired Then
                    Try
                        Await EnsureFlareSolverrSessionAsync(forceRecreate:=True)
                    Catch sessionEx As Exception
                        API_COC.DebugPrint($"[SESSIONS CRITICAL] Session recreation failed: {sessionEx.Message}")
                    End Try
                End If

                ' Exponential Backoff Delay
                If attempt < maxRetryAttempts Then
                    Dim currentDelay As Integer = baseDelayMilliseconds * CInt(Math.Pow(2, attempt - 1))
                    API_COC.DebugPrint($"[FLARESOLVERR RETRY] Sleeping for {currentDelay}ms before next engine attempt...")
                    Await Task.Delay(currentDelay)
                End If
            Next

            ' Wenn alle Versuche fehlschlagen, werfen wir eine Exception, die von der aufrufenden Funktion verarbeitet wird
            Throw New Exception($"[FLARESOLVERR CRITICAL] Exhausted all {maxRetryAttempts} attempts without retrieving target web content.")
        End Function
        Public Shared Async Function GetChocolateClanAsync(clanTag As String) As Task(Of String)
            API_COC.DebugPrint("[CHOCOLATE] Get Clan..." & clanTag)
            Dim blacklistResult As String = "MISMATCH"

            Try
                Dim encodedTag As String = clanTag.Trim().ToUpper().Replace("#", "")
                Dim targetUrl As String = $"https://cc.fwafarm.com/cc_n/clan.php?tag={encodedTag}"

                ' Kapselungsaufruf nutzen!
                Dim rawHtmlContent As String = Await GetWebpageFromFlareSolver2(targetUrl)

                ' Blacklist-Verarbeitung ausführen
                blacklistResult = GetBlackList(clanTag, rawHtmlContent)
                Console.WriteLine("jj_" & blacklistResult & "_jj")
                Return blacklistResult

            Catch ex As Exception
                API_COC.DebugPrint($"[CHOCOLATE CLAN CRITICAL] Pipeline breakdown: {ex.Message}")
                Console.WriteLine("jj_" & blacklistResult & "_jj")
                Return blacklistResult
            End Try
        End Function
        ' Die optimierte Blacklist-Prüffunktion
        Public Shared Function GetBlackList(ClanTag As String, webPage As String) As String
            If String.IsNullOrEmpty(webPage) Then
                Return "MISMATCH"
            End If

            Const startWord As String = "Association"
            Const endWord As String = "Current Members"

            Dim startIndex As Integer = webPage.IndexOf(startWord, StringComparison.OrdinalIgnoreCase)
            If startIndex = -1 Then
                API_COC.DebugPrint("[BLACKLIST] Startwort 'Association' nicht im HTML gefunden.")
                Return "MISMATCH"
            End If

            Dim cutStart As Integer = startIndex + startWord.Length
            Dim res As String = webPage.Substring(cutStart)

            Dim endIndex As Integer = res.IndexOf(endWord, StringComparison.OrdinalIgnoreCase)
            If endIndex = -1 Then
                API_COC.DebugPrint("[BLACKLIST] Endwort 'Current Members' nicht im HTML gefunden.")
                Return "MISMATCH"
            End If

            res = res.Substring(0, endIndex)

            If res.Contains("FWA Blacklisted", StringComparison.OrdinalIgnoreCase) OrElse
       res.Contains("Cross-League Blacklisted", StringComparison.OrdinalIgnoreCase) Then

                API_COC.DebugPrint($"[BLACKLIST SUCCESS] Clan {ClanTag} ist auf der BLACKLIST!")
                Return "BLACKLIST"
            Else
                API_COC.DebugPrint($"[BLACKLIST CLEAN] Keine Blacklist-Einträge für {ClanTag} gefunden.")
                Return "MISMATCH"
            End If
        End Function

        Public Shared Async Function GetChocolateClanWarAsync(clanTag As String) As Task(Of String)
            Dim winstring As String = String.Empty
            Dim win2string As String = String.Empty

            Try
                Dim encodedTag As String = clanTag.Trim().ToUpper().Replace("#", "")
                Dim targetUrl As String = $"https://points.fwafarm.com/clan?tag={encodedTag}"

                ' Kapselungsaufruf nutzen!
                Dim rawHtmlContent As String = Await GetWebpageFromFlareSolver(targetUrl)

                ' 4. Process layout data tree elements using HtmlAgilityPack
                Dim htmlDoc As New HtmlDocument()
                htmlDoc.LoadHtml(rawHtmlContent)

                Dim winnerBoxNode As HtmlNode = htmlDoc.DocumentNode.SelectSingleNode("//p[contains(@class, 'winner-box')]")
                Dim clanName1 As String = String.Empty
                Dim clanTag1 As String = String.Empty
                Dim clanName2 As String = String.Empty
                Dim clanTag2 As String = String.Empty
                Dim winnerClan As String = String.Empty
                Dim winnerTag As String = String.Empty

                If winnerBoxNode IsNot Nothing Then
                    Dim brNodes As HtmlNode() = If(winnerBoxNode.SelectNodes(".//br")?.ToArray(), New HtmlNode(-1) {})
                    For i As Integer = brNodes.Length - 1 To 0 Step -1
                        Dim brNode As HtmlNode = brNodes(i)
                        Dim textBreak As HtmlNode = htmlDoc.CreateTextNode(Environment.NewLine)
                        brNode.ParentNode.ReplaceChild(textBreak, brNode)
                    Next

                    Dim rawHtml As String = System.Net.WebUtility.HtmlDecode(winnerBoxNode.InnerHtml)
                    Dim clanRegex As New System.Text.RegularExpressions.Regex("([^<\n\r]+)\s*\(<a[^>]*tag=([^""]+)""[^>]*>[^<]+<\/a>\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    Dim matches As System.Text.RegularExpressions.MatchCollection = clanRegex.Matches(rawHtml)

                    If matches.Count >= 2 Then
                        Dim rawName1 As String = matches(0).Groups(1).Value
                        clanName1 = System.Text.RegularExpressions.Regex.Replace(rawName1, "<[^>]*>|[^<\n\r]*>", "").Trim()
                        clanTag1 = matches(0).Groups(2).Value.Trim()

                        Dim rawName2 As String = matches(1).Groups(1).Value
                        clanName2 = System.Text.RegularExpressions.Regex.Replace(rawName2, "<[^>]*>|[^<\n\r]*>", "").Trim()
                        If clanName2.ToLower().StartsWith("vs.") Then clanName2 = clanName2.Substring(3).Trim()
                        If clanName2.ToLower().StartsWith("vs") Then clanName2 = clanName2.Substring(2).Trim()

                        clanTag2 = matches(1).Groups(2).Value.Trim()
                    End If

                    Dim cleanText As String = System.Text.RegularExpressions.Regex.Replace(rawHtml, "<[^>]*>", " ")
                    Dim lines As String() = cleanText.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)

                    For Each line As String In lines
                        Dim trimmedLine As String = line.Trim()
                        If trimmedLine.ToLower().Contains("should win") OrElse trimmedLine.ToLower().Contains("winner") Then
                            winstring = trimmedLine
                            Exit For
                        End If
                    Next

                    If Not String.IsNullOrEmpty(winstring) Then
                        If Not String.IsNullOrEmpty(clanName1) AndAlso winstring.Contains(clanName1) Then
                            winnerClan = clanName1
                            winnerTag = clanTag1
                        ElseIf Not String.IsNullOrEmpty(clanName2) AndAlso winstring.Contains(clanName2) Then
                            winnerClan = clanName2
                            winnerTag = clanTag2
                        End If
                    End If
                End If

                Dim searchTagClean As String = clanTag.Trim().ToUpper().Replace("#", "")
                Dim winnerTagClean As String = winnerTag.Trim().ToUpper().Replace("#", "")

                Dim opponentTag As String = "UNKNOWN"
                Dim clanTag1Clean As String = clanTag1.Trim().ToUpper().Replace("#", "")
                Dim clanTag2Clean As String = clanTag2.Trim().ToUpper().Replace("#", "")

                If searchTagClean = clanTag1Clean Then
                    opponentTag = clanTag2
                ElseIf searchTagClean = clanTag2Clean Then
                    opponentTag = clanTag1
                End If

                If String.IsNullOrEmpty(winstring) Then
                    ' War ist live/open -> Blacklist des Gegners direkt prüfen
                    win2string = Await GetChocolateClanAsync(opponentTag)
                    winstring = win2string
                ElseIf String.IsNullOrEmpty(winnerTagClean) Then
                    win2string = "UNKNOWN"
                ElseIf winnerTagClean = searchTagClean Then
                    win2string = "WIN"
                Else
                    win2string = "LOOSE"
                End If

                Console.WriteLine("jj_" & win2string & "_jj")
                Return win2string

            Catch ex As Exception
                API_COC.DebugPrint($"[CHOCOLATE WAR CRITICAL] Pipeline breakdown: {ex.Message}")
                Console.WriteLine("jj_UNKNOWN_jj")
                Return "UNKNOWN"
            End Try
        End Function
    End Class

    Public Class ChocolateClash_API
        Public Shared Async Function RegisterCCAsync(client As DiscordSocketClient, guild As SocketGuild) As Task

            Dim ccCmd = New SlashCommandBuilder() With {
    .Name = "cc",
    .Description = "Downloads live FWA war data"
}.AddOption(New SlashCommandOptionBuilder() With {
    .Name = "clantag",
    .Description = "Type to search for a clan from the database...",
    .Type = ApplicationCommandOptionType.String,
    .IsRequired = True,
    .IsAutocomplete = True,
    .MinLength = 3, ' Verhindert zu kurze Fehleingaben
    .MaxLength = 12 ' Schützt vor übermäßig langen Strings
})
            Await guild.CreateApplicationCommandAsync(ccCmd.Build())

        End Function

        Public Shared Async Function HandleCCAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync(ephemeral:=False)

            Dim clanTagOption = command.Data.Options.FirstOrDefault(Function(o) o.Name = "clantag")
            If clanTagOption IsNot Nothing Then
                Dim clanTag As String = clanTagOption.Value.ToString()

                Dim ccBackgroundWorker = Task.Run(Async Function()
                                                      Dim isError As Boolean = False
                                                      Dim embedToBuild As EmbedBuilder = Nothing
                                                      Dim warningText As String = String.Empty
                                                      Dim opponentTagToAudit As String = String.Empty

                                                      Try
                                                          Dim predictionResult As String = Await ChocolateClashAPI.GetChocolateClanWarAsync(clanTag)

                                                          If Not String.IsNullOrEmpty(predictionResult) Then
                                                              Dim embedColor As Color = Color.LightGrey
                                                              Dim predictionText As String = String.Empty

                                                              Select Case predictionResult.ToUpper()
                                                                  Case "WIN"
                                                                      embedColor = Color.Green
                                                                      predictionText = "🟢 **Victory Predicted!**"

                                                                  Case "LOOSE"
                                                                      embedColor = Color.Red
                                                                      predictionText = "🔴 **Loose Predicted!**"

                                                                  Case "UNKNOWN"
                                                                      embedColor = Color.Orange
                                                                      predictionText = "🟡 **Status Unknown.** Calculation pending on FWA servers."
                                                                      ' FIX: Wir leeren die Audit-Variable, damit UNKNOWN niemals weitergereicht wird!
                                                                      opponentTagToAudit = String.Empty

                                                                  Case Else
                                                                      ' Ein echter Clan-Tag wurde zurückgegeben (War ist live aber ungeklärt)
                                                                      embedColor = Color.Blue
                                                                      opponentTagToAudit = predictionResult.ToUpper().Replace("#", "").Trim()
                                                                      predictionText = $"⏳ **Open Calculation : `{opponentTagToAudit}`"
                                                              End Select

                                                              embedToBuild = New EmbedBuilder() With {
                                                                          .Title = "📊 FWA War Live Prediction",
                                                                          .Description = "Live calculation matrix successfully synchronized from FWA infrastructure.",
                                                                          .Color = embedColor,
                                                                          .Timestamp = DateTimeOffset.Now
                                                                      }
                                                              embedToBuild.AddField("Target Clan Tag", $"`#{clanTag.ToUpper().Replace("#", "")}`", inline:=True)
                                                              embedToBuild.AddField("Prediction Outcome", predictionText, inline:=False)
                                                              embedToBuild.WithFooter(New EmbedFooterBuilder() With {.Text = "Pak Admin - FWA Prediction"})
                                                          Else
                                                              warningText = $"⚠️ **Warning:** No valid war metrics could be processed for `#{clanTag}`."
                                                          End If

                                                      Catch ex As Exception
                                                          API_COC.DebugPrint($"[DISCORD THREAD CRITICAL] Task failure: {ex.Message}")
                                                          isError = True
                                                      End Try

                                                      ' Initiale Nachricht an Discord senden
                                                      Dim activeMessage As RestFollowupMessage = Nothing
                                                      If isError Then
                                                          activeMessage = Await command.FollowupAsync("❌ An internal script timeout occurred while accessing FWA records.")
                                                      ElseIf Not String.IsNullOrEmpty(warningText) Then
                                                          activeMessage = Await command.FollowupAsync(warningText)
                                                      ElseIf embedToBuild IsNot Nothing Then
                                                          activeMessage = Await command.FollowupAsync(embed:=embedToBuild.Build())
                                                      End If

                                                      ' DYNAMISCHER PRÜF-BLOCK: Nur starten, wenn wir einen ECHTEN Tag haben (und kein UNKNOWN/WIN/LOOSE)
                                                      If Not String.IsNullOrEmpty(opponentTagToAudit) AndAlso
                                                                 opponentTagToAudit <> "UNKNOWN" AndAlso
                                                                 opponentTagToAudit.Length >= 6 AndAlso
                                                                 activeMessage IsNot Nothing Then

                                                          Dim backgroundAuditTask = Task.Run(Async Function()
                                                                                                 Try
                                                                                                     API_COC.DebugPrint($"[DISCORD AUDIT] Running background blacklist fetch for valid target: {opponentTagToAudit}")

                                                                                                     Dim blacklistStatus As String = Await ChocolateClashAPI.GetChocolateClanAsync(opponentTagToAudit)

                                                                                                     Dim updatedEmbed = New EmbedBuilder() With {
                                                                                                                 .Title = "📊 FWA War Live Prediction + Security Audit",
                                                                                                                 .Timestamp = DateTimeOffset.Now
                                                                                                             }

                                                                                                     Dim statusDetails As String = String.Empty
                                                                                                     If blacklistStatus.ToUpper() = "BLACKLIST" Then
                                                                                                         updatedEmbed.Color = Color.DarkRed
                                                                                                         statusDetails = $"🚨 **SECURITY ALERT:** Opponent `#{opponentTagToAudit}` is flagged on the **FWA BLACKLIST**!"
                                                                                                     Else
                                                                                                         updatedEmbed.Color = Color.Teal
                                                                                                         statusDetails = $"✅ **Security Clear:** Opponent `#{opponentTagToAudit}` passed the security audit safely."
                                                                                                     End If

                                                                                                     updatedEmbed.AddField("Target Clan Tag", $"`#{clanTag.ToUpper().Replace("#", "")}`", inline:=True)
                                                                                                     updatedEmbed.AddField("Prediction Outcome", $"⏳ **War Live.** No prediction set yet. Opponent tag: `#{opponentTagToAudit}`", inline:=False)
                                                                                                     updatedEmbed.AddField("Opponent Security Status", statusDetails, inline:=False)
                                                                                                     updatedEmbed.WithFooter(New EmbedFooterBuilder() With {.Text = "Pak Admin - Automated Security Verification"})

                                                                                                     Await activeMessage.ModifyAsync(Sub(x) x.Embed = updatedEmbed.Build())
                                                                                                 Catch pipelineEx As Exception
                                                                                                     API_COC.DebugPrint($"[AUDIT PIPELINE ERROR] Failed to perform background update: {pipelineEx.Message}")
                                                                                                 End Try
                                                                                             End Function)
                                                      End If
                                                  End Function)
            End If


        End Function


    End Class

End Module