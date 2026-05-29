Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq ' Make sure Newtonsoft.Json NuGet package is installed

Public Class CocService
    ' Speichert den aktuellen API-Token zentral für alle Klassen
    Public Shared Property apiToken As String = ""
End Class
Public Class API_COC
    ' Persistent asynchronous HttpClient using a CookieContainer to maintain the session
    Private Shared ReadOnly _cookieContainer As New CookieContainer()
    Private Shared ReadOnly _handler As New HttpClientHandler() With {.CookieContainer = _cookieContainer}
    Private Shared ReadOnly _httpClient As New HttpClient(_handler)

    Private Shared myIP As String = "0"
    Private Const Json_Empty As String = "{}"

    ''' <summary>
    ''' Writes debug and execution logs to a local log file.
    ''' </summary>
    Public Shared Sub DebugPrint(s As String)
        Console.WriteLine($"{DateTime.Now}: {s}")

    End Sub

    ''' <summary>
    ''' Converts the Clash of Clans API timestamp string to a local DateTime object.
    ''' </summary>
    Public Shared Function COCTimeToUTC(s As String) As DateTime
        Try
            Dim year = s.Substring(0, 4)
            Dim month = s.Substring(4, 2)
            Dim day = s.Substring(6, 2)
            Dim hour = s.Substring(9, 2)
            Dim min = s.Substring(11, 2)
            Dim sec = s.Substring(13, 2)

            Dim endt As DateTime = Convert.ToDateTime($"{day}.{month}.{year} {hour}:{min}:{sec}")
            endt = endt.AddSeconds((DateTime.Now - DateTime.UtcNow).TotalSeconds)
            Return endt
        Catch ex As Exception
            Return DateTime.Now
        End Try
    End Function

#Region "JSON Model Classes"
    Private Class Keyinfo
        Public Property status As Status
        Public Property sessionExpiresInSeconds As Integer
        Public Property keys As KeyList()
    End Class
    Private Class Status
        Public Property code As Integer
        Public Property message As String
        Public Property detail As Object
    End Class
    Private Class KeyList
        Public Property id As String
        Public Property name As String
        Public Property key As String
        Public Property cidrRanges As String()
    End Class
    Private Class AddKey
        Public Property cidrRanges As String()
        Public Property description As String
        Public Property name As String
        Public Property scopes As String()
    End Class
    Private Class Login
        Public Property email As String
        Public Property password As String
    End Class
    Private Class KeyId
        Public Property id As String
    End Class
#End Region

    ''' <summary>
    ''' Dynamically retrieves the current public external IP address.
    ''' </summary>
    Private Shared Async Function GetExternalIpAsync() As Task(Of String)
        Try
            ' Primary fast endpoint
            Dim externalIP As String = Await _httpClient.GetStringAsync("http://checkip.dyndns.org/")
            externalIP = (New Regex("\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}")) _
                     .Matches(externalIP)(0).ToString()
            Console.WriteLine(externalIP)
            Return externalIP.Trim()
            Return Regex.Match(externalIP, "\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}").Value
            Return externalIP.Trim()
        Catch
            ' Secondary backup plain-text endpoint

        End Try
        Try
            Dim html As String = Await _httpClient.GetStringAsync("https://icanhazip.com")

            ' Regex clean extraction of the IP address string
            Return Regex.Match(html, "\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}").Value
        Catch
            Return "--"
        End Try
    End Function

    ''' <summary>
    ''' Sends an asynchronous HTTP POST request with a JSON payload.
    ''' </summary>
    Private Shared Async Function PostJsonAsync(addr As String, json_data As String) As Task(Of String)
        Try
            Dim content As New StringContent(json_data, Encoding.UTF8, "application/json")
            Dim response As HttpResponseMessage = Await _httpClient.PostAsync(addr, content)
            Return Await response.Content.ReadAsStringAsync()
        Catch ex As Exception
            ' FIX BC36943: Pure synchronous logging without Await tokens inside Catch block!
            DebugPrint("[HTTP-ERROR] Post failed at " & addr & ": " & ex.Message)
            Return "{}"
        End Try
    End Function

    ''' <summary>
    ''' Checks your current IP address and dynamically updates the Supercell API keys if it changed.
    ''' </summary>
    Public Shared Async Function UpdateKeysAsync() As Task(Of Boolean)
        Dim post_result As String
        Dim new_ip As String = Await GetExternalIpAsync()

        If new_ip = "--" Then
            DebugPrint("External IP Address is: offline " & myIP)
            Return False
        End If
        Console.WriteLine("Current  external IP: " & new_ip)
        ' If the IP has not changed, skip the portal execution sequence entirely
        If new_ip <> myIP Then
            DebugPrint($"IP changed from {myIP} to {new_ip}")
            myIP = new_ip
        Else
            Return True
        End If

        ' --- READ CREDENTIALS FROM \config\coclogin.txt ---
        Dim supercellEmail As String = ""
        Dim supercellPassword As String = ""

        Try
            Dim baseDirectory As String = AppDomain.CurrentDomain.BaseDirectory
            Dim configFilePath As String = Path.Combine(baseDirectory, "config", "coclogin.txt")

            If Not File.Exists(configFilePath) Then
                DebugPrint($"[ERROR] Configuration file missing at: {configFilePath}")
                Return False
            End If

            Dim lines As String() = File.ReadAllLines(configFilePath)

            If lines.Length < 2 Then
                DebugPrint("[ERROR] coclogin.txt must contain Email in line 1 and Password in line 2!")
                Return False
            End If

            supercellEmail = lines(0).Trim()
            supercellPassword = lines(1).Trim()

        Catch ex As Exception
            DebugPrint("[ERROR] Failed to read Supercell credentials file: " & ex.Message)
            Return False
        End Try
        ' --------------------------------------------------

        DebugPrint("Logging into Supercell Developer Portal...")
        Dim loginData As New Login With {
            .email = supercellEmail,
            .password = supercellPassword
        }

        ' 1. Perform login authentication against the API endpoint
        Await PostJsonAsync("https://developer.clashofclans.com/api/login", JsonConvert.SerializeObject(loginData, Formatting.None))

        ' 2. Retrieve all active API Keys
        post_result = Await PostJsonAsync("https://developer.clashofclans.com/api/apikey/list", Json_Empty)

        Dim keyinfo As Keyinfo = JsonConvert.DeserializeObject(Of Keyinfo)(post_result)
        Dim keyfound As Boolean = False

        If keyinfo IsNot Nothing AndAlso keyinfo.keys IsNot Nothing Then
            For Each key As KeyList In keyinfo.keys
                ' If a key already exists with the current correct IP, bind it directly
                If key.cidrRanges IsNot Nothing AndAlso key.cidrRanges.Count = 1 AndAlso key.cidrRanges(0) = myIP Then
                    Console.WriteLine("K" & key.cidrRanges(0))
                    DebugPrint("Key status OK: " & key.name)
                    CocService.apiToken = key.key
                Else
                    ' IP mismatch -> Delete the old expired key
                    Dim del_key As New KeyId With {.id = key.id}

                    DebugPrint($"DELETING Key {key.name} ({key.id}) due to outdated IP.")
                    Await PostJsonAsync("https://developer.clashofclans.com/api/apikey/revoke", JsonConvert.SerializeObject(del_key, Formatting.None))

                    ' Instantly recreate the key with the new IP address bound
                    DebugPrint("Recreating Key: " & key.name)
                    Dim new_key As New AddKey With {
                        .name = key.name,
                        .description = "Automated dynamic IP update: " & DateTime.Now.ToString(),
                        .cidrRanges = {myIP},
                        .scopes = {"clash"}
                    }
                    Dim createResult As String = Await PostJsonAsync("https://developer.clashofclans.com/api/apikey/create", JsonConvert.SerializeObject(new_key, Formatting.None))

                    ' Fresh token extraction out of the creation payload response
                    Dim keyData = JsonConvert.DeserializeObject(Of Keyinfo)(createResult)
                    Console.WriteLine(keyData?.keys?.Length)
                    If keyData?.keys?.Length > 0 Then
                        CocService.apiToken = keyData.keys(0).key
                        Console.WriteLine(keyData.keys(0).key)
                    End If
                End If

                If key.name = "MyAppKey" Then
                    keyfound = True
                End If
            Next
        End If

        ' If "MyAppKey" doesn't exist anywhere on this profile, create it from scratch
        If Not keyfound Then
            DebugPrint("Creating token 'MyAppKey' from scratch...")
            Dim MyAppKey As New AddKey With {
                .name = "MyAppKey",
                .description = "My own automated key " & DateTime.Now.ToString(),
                .cidrRanges = {myIP},
                .scopes = {"clash"}
            }
            Await PostJsonAsync("https://developer.clashofclans.com/api/apikey/create", JsonConvert.SerializeObject(MyAppKey, Formatting.None))
        End If

        ' 3. Log out safely from the active web session token handler
        Await PostJsonAsync("https://developer.clashofclans.com/api/logout", Json_Empty)
        DebugPrint("Done. Session closed successfully.")
        Return True
    End Function
End Class

Public Class ClashOfClansAPI
    Private ReadOnly _httpClient As HttpClient
    Private ReadOnly _apiToken As String
    Const BaseUrl As String = "https://api.clashofclans.com/v1/"

    ''' <summary>
    ''' Initializes the API client with your secret Supercell Developer Token.
    ''' </summary>
    ''' <param name="apiToken">The authorization token provided by ://clashofclans.com</param>
    Public Sub New(apiToken As String)
        _apiToken = apiToken
        _httpClient = New HttpClient()

        ' Configure HTTP headers required by the Clash of Clans API
        _httpClient.DefaultRequestHeaders.Accept.Clear()
        _httpClient.DefaultRequestHeaders.Accept.Add(New MediaTypeWithQualityHeaderValue("application/json"))
        _httpClient.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", _apiToken)
    End Sub

    ''' <summary>
    ''' Fetches the current member count of a clan asynchronously using its unique clan tag.
    ''' </summary>
    ''' <param name="clanTag">The clan tag (e.g., #2QR0Q8QYL or 2QR0Q8QYL)</param>
    ''' <returns>The total number of members as an Integer, or -1 if the request fails.</returns>
    Public Async Function GetMemberCountAsync(clanTag As String) As Task(Of Integer)
        Try
            ' Format the clan tag for the URL endpoint (the '#' must be URL-encoded as '%23')
            Dim cleanTag As String = clanTag.Trim().Replace("#", "%23")
            Dim url As String = $"https://api.clashofclans.com/v1/clans/{cleanTag}"
            Console.WriteLine(_httpClient.DefaultRequestHeaders)
            Console.WriteLine(url)
            ' Send the GET request asynchronously
            Dim response As HttpResponseMessage = Await _httpClient.GetAsync(url)

            ' Evaluate the HTTP response status
            '  RICHTIG (Fehler behoben):
            If response.IsSuccessStatusCode Then
                ' Read the JSON body content as a raw string
                Dim jsonString As String = Await response.Content.ReadAsStringAsync()

                ' Parse the JSON payload
                Using doc As JsonDocument = JsonDocument.Parse(jsonString)
                    Dim root As JsonElement = doc.RootElement
                    Dim membersProperty As JsonElement = Nothing

                    ' Safely extract the "members" value
                    If root.TryGetProperty("members", membersProperty) Then
                        Return membersProperty.GetInt32()
                    End If
                End Using ' Schließt das "Using", NICHT das "If"!

            Else '  Dieses "Else" gehört nun wieder korrekt zum "If response.IsSuccessStatusCode"
                Console.WriteLine($"[API-ERROR] HTTP Status: {response.StatusCode} for tag {clanTag}")
            End If '  ERST HIER wird das Haupt-If geschlossen!

        Catch ex As Exception
            Console.WriteLine($"[API-EXCEPTION] Failed to retrieve member count: {ex.Message}")
        End Try

        ' Returning -1 indicates an error occurred during the process
        Return -1
    End Function

    ''' <summary>
    ''' Fetches live data from the Supercell Clash of Clans API.
    ''' </summary>
    Public Async Function GetClanDataAsync(clanTag As String) As Task(Of JObject)
        ' URL encode the hash symbol (#52LJV8 becomes %2352LJV8)
        Dim sanitizedTag As String = clanTag.Replace("#", "%23")
        Dim endpoint As String = $"{BaseUrl}clans/{sanitizedTag}"

        Try
            Dim response As HttpResponseMessage = Await _httpClient.GetAsync(endpoint)
            If response.IsSuccessStatusCode Then
                Dim jsonString As String = Await response.Content.ReadAsStringAsync()
                Return JObject.Parse(jsonString)
            Else
                Console.WriteLine($"CoC API returned error status: {response.StatusCode} for clan {clanTag}")
                Return Nothing
            End If
        Catch ex As Exception
            Console.WriteLine($"Network Exception during CoC API fetch: {ex.Message}")
            Return Nothing
        End Try
    End Function

End Class

