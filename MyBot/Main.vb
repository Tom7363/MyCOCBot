Imports System
Imports System.IO
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket

Module Main
    Private _client As DiscordSocketClient
    Private _commandHandler As CommandHandler
    Private _readyHandler As ReadyHandler ' <-- Added reference to the new class
    Private ReadOnly LogPath As String = Path.Combine(AppContext.BaseDirectory, "coc_log.txt")

    Sub Main(args As String())
        ' Starts the asynchronous bot process
        MainAsync().GetAwaiter().GetResult()
    End Sub

    Async Function MainAsync() As Task
        ' Configure the bot with essential gateway intents
        Dim config = New DiscordSocketConfig() With {
            .GatewayIntents = GatewayIntents.Guilds Or GatewayIntents.GuildMembers,
            .AlwaysDownloadUsers = True
        }

        _client = New DiscordSocketClient(config)

        ' Initialize handlers and inject the client dependency
        _commandHandler = New CommandHandler(_client)
        _readyHandler = New ReadyHandler(_client) ' <-- Initialize the ready handler

        ' Register event handlers to their separate classes
        AddHandler _client.Log, AddressOf LogAsync
        AddHandler _client.Ready, AddressOf _readyHandler.HandleClientReadyAsync ' <-- Redirected to class
        AddHandler _client.SlashCommandExecuted, AddressOf _commandHandler.HandleSlashCommandAsync

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

        ' Keeps the application running on your Oracle Linux VM
        Await Task.Delay(-1)
    End Function

    ' =========================================================================
    ' LOGGING ENGINE
    ' =========================================================================
    Private Function LogAsync(message As LogMessage) As Task
        Task.Run(Sub()
                     Dim logLine As String = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{message.Severity}] {message.Source}: {message.Message}"
                     Console.WriteLine(logLine)

                     Try
                         File.AppendAllText(LogPath, logLine & Environment.NewLine)
                     Catch
                         ' Ignore if log file is temporarily locked
                     End Try
                 End Sub)
        Return Task.CompletedTask
    End Function
End Module
