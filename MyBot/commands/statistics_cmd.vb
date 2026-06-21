Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Discord
Imports Discord.WebSocket

Public Module Statistics

    Public Class StatusCommands

        Public Shared Async Function RegisterPingCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            ' /ping
            Dim pingCmd = New SlashCommandBuilder() With {
                                     .Name = "ping",
                                     .Description = "Check connection, bot version, and live latency"
                                 }
            Await guild.CreateApplicationCommandAsync(pingCmd.Build())

        End Function
        Public Shared Async Function RegisterStatusCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            Dim statusCommandBuilder As New SlashCommandBuilder() With {
                                 .Name = "status",
                                 .Description = "Displays the resource utilization of the bot, server, and Oracle DB."
                                 }
            Await guild.CreateApplicationCommandAsync(statusCommandBuilder.Build())

        End Function

        Public Shared Async Function HandlePingCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            ' -----------------------------------------------------------------
            ' COMMAND: /ping
            ' -----------------------------------------------------------------
            Const Version As String = "01.00.00 F"
            Dim latency As Integer = client.Latency
            Dim osDescription As String = System.Runtime.InteropServices.RuntimeInformation.OSDescription

            Dim DBConnectionStatus As String = If(OracleDatabaseManager.IsDBConnected(), "Database Connected 🟢", "Database Disconnected 🔴")

            Dim responseMessage As String = $"Hello, I am here V {Version} 🚀{Environment.NewLine}" &
                   $"• **Status:** Online 🟢{Environment.NewLine}" &
                   $"• **Latency:** `{latency} ms`{Environment.NewLine}" &
                   $"• **OS:** `{osDescription}`{Environment.NewLine}" &
                   $"• **DB:** `{DBConnectionStatus}`{Environment.NewLine}" &
                   $"• **System:** `Pak Admin Bot System`"


            Await command.RespondAsync(responseMessage)
            API_COC.DebugPrint($"/ping used by {command.User.Username} (Latency: {latency}ms)")
        End Function
        Public Shared Async Function HandleStatusCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            ' Add Await here since it pulls live data from Oracle DB
            Dim embedBuilder = Await GetResourceUsageEmbedAsync(client)
            ' Send response to user
            Await command.RespondAsync(embed:=embedBuilder.Build())
        End Function
        Public Shared Async Function GetResourceUsageEmbedAsync(client As DiscordSocketClient) As Task(Of EmbedBuilder)
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


    End Class

End Module