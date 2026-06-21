Imports System
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection.PortableExecutable
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Timers
Imports Discord
Imports Discord.Rest
Imports Discord.Webhook
Imports Discord.WebSocket
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client
Public Class CommandHandler
    Private ReadOnly _client As DiscordSocketClient

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
                'Statistics Commands
                Case "ping"
                    Await StatusCommands.HandlePingCommandAsync(command, _client)
                Case "status"
                    Await StatusCommands.HandleStatusCommandAsync(command, _client)
                Case "roles"
                    Await DiscordServerCommands.HandleRolesCommandAsync(command, _client)
                Case "channels"
                    Await DiscordServerCommands.HandleChannelsCommandAsync(command, _client)
                Case "whois"
                    Await DiscordServerCommands.HandleWhoIsCommandAsync(command, _client)
                'Discord Thread Commands 
                Case "threadembed"
                    Await DiscordThreads.HandleThreadEmbedCommandAsync(command, _client)
                Case "deletethread"
                    Await DiscordThreads.HandleDeleteThreadCommandAsync(command, _client)
                Case "movetothread"
                    Await DiscordThreads.HandleMoveToThreadCommandAsync(command, _client)
                 'Template Commands
                Case "template"
                    Await TemplateCommands.HandleTemplateCommandAsync(command)
                Case "news"
                    Await TemplateCommands.HandleNewsCommandAsync(command)
                Case "showclans"
                    Await ClanManager.HandleClanListAsync(command)
                Case "clan-add"
                    Await ClanManager.HandleClanAddAsync(command)
                Case "clan-remove"
                    Await ClanManager.HandleClanRemoveAsync(command)
                Case "clan-list"
                    Await ClanManager.HandleClanListAsync(command)
                Case "dump"
                    Await ClanManager.HandleDumpListAsync(command)
                Case "cl"
                    Await ClanManager.HandleClCommandAsync(command)
                Case "layout"
                    Await Baselayouts.HandleLayoutCommandAsync(command)
                Case "bases"
                    Await Baselayouts.HandleBasesCommandAsync(command)
                Case "layout-add"
                    Await Baselayouts.HandleLayoutAddAsync(command)

                Case "roaster-create"
                    Await CWLRoaster.HandleRosterCreateAsync(command)
                Case "roaster-get"
                    Await CWLRoaster.HandleRosterGetAsync(command)
                Case "cwl-info"
                    Await CWLRoaster.HandleCWLInfoAsync(command)
                Case "weight-update"
                    Await FWAStats_API.HandleWeigthUpdateAsync(command)
                Case "cc"
                    Await ChocolateClash_API.HandleCCAsync(command)
                Case "cwl-status"
                    Await CWLRoaster.HandleCWLStatusAsync(command, _client)

                Case Else
                    Await command.RespondAsync("❌ Unknown command.", ephemeral:=True)
            End Select
        Catch ex As Exception
            API_COC.DebugPrint($"[CRITICAL] Command execution failed: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return
    End Function

    Public Async Function HandleAutocompleteAsync(autocomplete As SocketAutocompleteInteraction) As Task
        Select Case autocomplete.Data.CommandName
            Case "cl", "cc"
                Await ClanManager.HandleAutoCompleteClansAsync(autocomplete)
            Case "layout"
                Await Baselayouts.HandleBaseLayoutAsync(autocomplete)
            Case "whois"
                Await DiscordServerCommands.HandleWhoIsAutocompleteAsync(autocomplete)
        End Select
    End Function
    ''' <summary>
    ''' Intercepts and processes component interactions (like buttons) triggered in Discord channels.
    ''' </summary>
    Public Async Function HandleButtonExecutionAsync(component As SocketMessageComponent) As Task
        Select Case component.Data.CustomId
            Case "refresh_cwl_info"
                Await CWLRoaster.HandleCWLInfoUpdate(component)
            Case "refresh_cwl_status"
                Await CWLRoaster.HandleCWLStatusUpdate(component)
            Case "refresh_clan_list"
                Await ClanManager.HandleClanlistUpdate(component)
        End Select
    End Function
End Class
