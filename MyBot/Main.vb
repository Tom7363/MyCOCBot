'Version 01.00.00 E
'   - Fixed Token for IP Adress Coc API on First Start false
'   - Fixed Create Weight Table
'   - Fixed Create Roaster
'  DNE Running On Ampere Instance 
'  Added avatar For news
'Version 1.0.00 F
'  command / cc clantag
'  Fixed DB Inactive
'  Added Embed Clans Refresh Button
'  Fixed 100% CPU usage


Imports System
Imports System.IO
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket

Module Main
    Private _client As DiscordSocketClient
    Private _commandHandler As CommandHandler
    Private _readyHandler As ReadyHandler
    Private ReadOnly LogPath As String = Path.Combine(AppContext.BaseDirectory, "coc_log.txt")

    ' Prevents multiple reconnection loops from running simultaneously
    Private IsReconnecting As Boolean = False

    Sub Main(args As String())
        ' 1. Wire up the OS shutdown and application close hooks instantly
        AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit

        ' Starts the asynchronous bot process
        MainAsync().GetAwaiter().GetResult()
    End Sub

    ''' <summary>
    ''' Synchronous event handler executed by the OS when the application process is terminated.
    ''' </summary>
    Private Sub OnProcessExit(sender As Object, e As EventArgs)
        Console.WriteLine("[SHUTDOWN] Application exit signal received. Starting resource cleanup...")
        Try
            ' CRITICAL FIX: Changed from '_commandHandler' instance to the static global 'API_COC' context.
            ' This guarantees execution during OS shutdown even if the command handler is not yet instantiated.
            ChocolateClashAPI.DestroyFlareSolverrSessionAsync().GetAwaiter().GetResult()
            Console.WriteLine("[SHUTDOWN SUCCESS] FlareSolverr session destroyed. RAM released from Oracle VM.")
        Catch ex As Exception
            Console.WriteLine($"[SHUTDOWN ERROR] Failed to clean up FlareSolverr session: {ex.Message}")
        End Try
    End Sub
    Private _keepAliveTimer As System.Threading.Timer

    Async Function MainAsync() As Task
        Console.WriteLine("Checking external IP and Supercell API tokens...")
        API_COC.DebugPrint("Bot starting - performing initial IP and Token check.")

        Dim keyUpdateSuccess As Boolean = Await API_COC.UpdateKeysAsync()

        If keyUpdateSuccess Then
            Console.WriteLine($"Token check complete. Active Token: {CocService.apiToken.Substring(0, Math.Min(10, CocService.apiToken.Length))}...")
            API_COC.DebugPrint("Initial token update successful.")
        Else
            Console.WriteLine("⚠️ WARNING: Token update failed. API requests might fail!")
            API_COC.DebugPrint("Initial token update failed.")
        End If

        ' Configure the bot with essential gateway intents and connection tuning
        Dim config = New DiscordSocketConfig() With {
            .GatewayIntents = GatewayIntents.Guilds Or GatewayIntents.GuildMembers,
            .AlwaysDownloadUsers = True,
            .MessageCacheSize = 50
        }
        _client = New DiscordSocketClient(config)

        ' OPTIMIZATION: Handle the Disconnected event to break the infinite "Failed to resume" loop
        AddHandler _client.Disconnected, AddressOf HandleGatewayDisconnectAsync

        ' Initialize handlers and inject the client dependency
        _commandHandler = New CommandHandler(_client)
        _readyHandler = New ReadyHandler(_client)

        ' Register event handlers to their separate classes
        AddHandler _client.Log, AddressOf LogAsync
        AddHandler _client.Ready, AddressOf _readyHandler.HandleClientReadyAsync
        AddHandler _client.SlashCommandExecuted, AddressOf _commandHandler.HandleSlashCommandAsync
        AddHandler _client.AutocompleteExecuted, AddressOf _commandHandler.HandleAutocompleteAsync

        ' Register the button execution event framework within MainAsync
        AddHandler _client.ButtonExecuted, AddressOf _commandHandler.HandleButtonExecutionAsync

        ' Path to the token file in the configuration subdirectory
        Dim tokenPath As String = Path.Combine(AppContext.BaseDirectory, "config", "token.txt")
        Dim token As String = ""

        Try
            If File.Exists(tokenPath) Then
                token = File.ReadAllText(tokenPath).Trim()
            Else
                Console.WriteLine($"[ERROR] Token file not found at: {tokenPath}")
                Return
            End If
        Catch ex As Exception
            Console.WriteLine($"[ERROR] Failed to read token file: {ex.Message}")
            Return
        End Try

        Await _client.LoginAsync(TokenType.Bot, token)
        Await _client.StartAsync()
        Await OracleDatabaseManager.ConnectDBAsync()

        ' =========================================================================
        ' ORACLE CLOUD KEEP-ALIVE ENGINE (Prevents Stale Connection Timeouts)
        ' =========================================================================
        _keepAliveTimer = New System.Threading.Timer(
        Async Sub(state)
            Try
                Await OracleDatabaseManager.KeepDatabaseAliveAsync()
                API_COC.DebugPrint("Database Keep-Alive: Connection active and validated.")
            Catch ex As Exception
                API_COC.DebugPrint($"[KEEP-ALIVE WARNING] Database connection dropped, auto-recovering: {ex.Message}")
            End Try
        End Sub,
        Nothing,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(10))

        ' Keeps the application running on your Oracle Linux VM smoothly
        Await Task.Delay(-1)
    End Function

    ''' <summary>
    ''' Safely handles the Discord gateway disconnect signal.
    ''' Offloads the reconnection sequence to a background thread to prevent blocking Discord.Net.
    ''' </summary>
    Private Function HandleGatewayDisconnectAsync(ex As Exception) As Task
        ' If a reconnection thread is already running, instantly exit to save CPU resources
        If IsReconnecting Then Return Task.CompletedTask

        IsReconnecting = True
        API_COC.DebugPrint($"[GATEWAY DISCONNECT] Reason: {ex.Message}. Offloading recovery process...")

        ' CRITICAL FIX: Run the heavy delay and reconnect logic inside a background task.
        ' This allows the Disconnected event handler to return immediately, resolving the blocking warning.
        Task.Run(Async Function()
                     Try
                         ' Give the network stack and Discord Gateway 10 seconds to fully reset
                         ' This task delay yields the thread back to the Linux Kernel pool efficiently
                         Await Task.Delay(10000)

                         API_COC.DebugPrint("[GATEWAY] Triggering clean StartAsync reconnect sequence...")
                         ' StartAsync handles internal resume state natively
                         Await _client.StartAsync()
                     Catch logEx As Exception
                         API_COC.DebugPrint($"[GATEWAY RESET ERROR] Reconnection attempt failed: {logEx.Message}")
                     Finally
                         ' Safely release the execution lock for future disconnection cycles
                         IsReconnecting = False
                     End Try
                 End Function)

        ' Return directly to unblock Discord.Net's internal event loop instantly
        Return Task.CompletedTask
    End Function
    ' =========================================================================
    ' LOGGING ENGINE
    ' =========================================================================
    ''' <summary>
    ''' Intercepts Discord core log events. Handled via non-blocking asynchronous I/O pipelines.
    ''' </summary>
    Private Function LogAsync(message As LogMessage) As Task
        Dim logLine As String = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{message.Severity}] {message.Source}: {message.Message}"
        Console.WriteLine(logLine)

        Try
            ' Use synchronous fallback append to ensure file writing without stalling the main event loops
            File.AppendAllText(LogPath, logLine & Environment.NewLine)
        Catch
            ' Ignore if log file is temporarily locked by another system thread allocation
        End Try

        Return Task.CompletedTask
    End Function
End Module
