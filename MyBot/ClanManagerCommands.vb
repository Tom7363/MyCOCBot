Imports System
Imports System.ComponentModel
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket
Imports MyCocBot.CocModels
Imports Newtonsoft.Json.Linq
Imports Oracle.ManagedDataAccess.Client

Public Module ClanManagerCommands
    Public Class ClanManager
        Public Shared Async Function RegisterClanmanagerCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            ' /showclans
            Dim showClansCmd = New SlashCommandBuilder() With {
                                     .Name = "showclans",
                                     .Description = "Displays all registered Clash of Clans clans from the Oracle database"
                                 }


            ' Command Structure setup: /clan-add [tag] [category]
            Dim clanAddCmd As New SlashCommandBuilder()
            clanAddCmd.WithName("clan-add")
            clanAddCmd.WithDescription("Adds a brand new clan target entry to the active guild database layout.")
            clanAddCmd.AddOption("tag", ApplicationCommandOptionType.String, "The structural tag id belonging to the clan (e.g. #52LJV8)", isRequired:=True)

            ' Establish custom restriction array inputs (Choices dropdown)
            Dim categoryOption As New SlashCommandOptionBuilder() With {
                                .Name = "category",
                                .Description = "The functional operating category mapping for this clan tracking stream.",
                                .Type = ApplicationCommandOptionType.String,
                                .IsRequired = True
                                }

            categoryOption.AddChoice("FWA", "FWA")
            categoryOption.AddChoice("CWL", "CWL")
            categoryOption.AddChoice("CWL Backup", "CWL Backup")

            clanAddCmd.AddOption(categoryOption)

            ' Command Structure setup: /clan-remove [tag]
            Dim clanRemoveCmd As New SlashCommandBuilder()
            clanRemoveCmd.WithName("clan-remove")
            clanRemoveCmd.WithDescription("Removes a registered clan target entry away from the server tracking logs.")
            clanRemoveCmd.AddOption("tag", ApplicationCommandOptionType.String, "The specific targeted tracker tag you want dropped.", isRequired:=True)

            ' Command Structure setup: /clan-list
            Dim clanListCmd As New SlashCommandBuilder()
            clanListCmd.WithName("clan-list")
            clanListCmd.WithDescription("Displays a comprehensive list of all verified clan entries registered here.")
            ' Command Structure setup: /clan-list
            Dim dumpListCmd As New SlashCommandBuilder()
            dumpListCmd.WithName("dump")
            dumpListCmd.WithDescription("Displays a comprehensive list of all clans to dump capital gold.")

            ' Command Structure: /cl [clan]
            Dim clCmd As New SlashCommandBuilder()
            clCmd.WithName("cl")
            clCmd.WithDescription("Shows the direct link to join a specific tracked clan.")

            ' Important: Set IsAutocomplete = True
            clCmd.AddOption("clan", ApplicationCommandOptionType.String, "Type to search for a clan from the database...", isRequired:=True, isAutocomplete:=True)
            Await guild.CreateApplicationCommandAsync(clCmd.Build())
            Await guild.CreateApplicationCommandAsync(clanListCmd.Build())
            Await guild.CreateApplicationCommandAsync(dumpListCmd.Build())
            Await guild.CreateApplicationCommandAsync(clanAddCmd.Build())
            Await guild.CreateApplicationCommandAsync(clanRemoveCmd.Build())

            ' Zusammen mit den anderen Commands an Discord senden
            Await guild.CreateApplicationCommandAsync(showClansCmd.Build())

        End Function

        ''' <summary>
        ''' Evaluates and saves an added clan via /clan-add [tag] [category]
        ''' </summary>
        Public Shared Async Function HandleClanAddAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()
            Dim guildUser = TryCast(command.User, SocketGuildUser)
            If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

                Dim clanTag As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "tag")?.Value.ToString().ToUpper().Replace(" ", "")
                Dim category As String = command.Data.Options.FirstOrDefault(Function(o) o.Name = "category")?.Value.ToString()

                If Not clanTag.StartsWith("#") Then clanTag = "#" & clanTag

                ' 1. Check validation through external Supercell API endpoint
                Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

                Dim clanData As JObject = Await cocApi.GetClanDataAsync(clanTag)
                If clanData Is Nothing Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ Clan tag `{clanTag}` could not be found within the Clash of Clans API system.")
                    Return
                End If

                Dim clanName As String = clanData("name")?.ToString()

                ' 2. Persist using the database helper functions
                Dim success As Boolean = Await OracleDatabaseManager.AddClanAsync(command.GuildId.Value, clanTag, clanName, category)
                If success Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"✅ **{clanName}** (`{clanTag}`) successfully registered as a **{category}** clan!")
                Else
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "❌ A database operation error occurred while processing the save request.")
                End If
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If
        End Function
        ''' <summary>
        ''' Handles removing records via /clan-remove [tag]
        ''' </summary>
        Public Shared Async Function HandleClanRemoveAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()
            Dim guildUser = TryCast(command.User, SocketGuildUser)
            If guildUser IsNot Nothing AndAlso guildUser.Roles.Any(Function(r) r.Name = "Server Orga") Then

                Dim clanTag As String = command.Data.Options.First().Value.ToString().ToUpper().Replace(" ", "")
                If Not clanTag.StartsWith("#") Then clanTag = "#" & clanTag

                Dim success As Boolean = Await OracleDatabaseManager.RemoveClanAsync(command.GuildId.Value, clanTag)
                If success Then
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"🗑️ Clan `{clanTag}` has been successfully removed from this server's database entries.")
                Else
                    Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = $"❌ Clan `{clanTag}` was not found or could not be removed from data tables.")
                End If
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If
        End Function
        ''' <summary>
        ''' Central core logic engine that queries database/API and generates the fresh embed and button layouts.
        ''' </summary>
        Private Shared Async Function BuildClanListMessageAsync(guildId As ULong) As Task(Of Tuple(Of Embed, MessageComponent))
            ' 1. Fetch tracked clans from Oracle DB
            Dim clans = Await OracleDatabaseManager.GetClansAsync(guildId)

            If clans.Count = 0 Then
                Return Nothing
            End If

            ' Lists to separate types dynamically
            Dim cwlClans As New List(Of Tuple(Of String, String))()
            Dim fwaClans As New List(Of Tuple(Of String, String))()
            Dim backupClans As New List(Of Tuple(Of String, String))()

            Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

            ' 2. Categorize and fetch live member counts from Supercell API
            For Each clan In clans
                Dim tag As String = clan.Item1
                Dim name As String = clan.Item2
                Dim cat As String = clan.Item3

                Dim memberCount As String = "N/A"


                ' Fetch live member count from API
                Dim clanData As JObject = Await cocApi.GetClanDataAsync(tag)
                If clanData IsNot Nothing AndAlso clanData("members") IsNot Nothing Then
                    memberCount = clanData("members").ToString()
                End If

                ' Create the official CoC Deep Link (remove '#' for the URL query parameter)
                Dim cleanTagForUrl As String = tag.Replace("#", "%23")
                Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTagForUrl}"
                ' Format the line using Discord Markdown link style: [Text](URL)
                Dim formattedLine As String = $"[{name} ({tag})]({cocDeepLink}) - {memberCount}"

                ' Group into respective categories
                If cat.Equals("CWL", StringComparison.OrdinalIgnoreCase) Then
                    cwlClans.Add(Tuple.Create(name, formattedLine))
                ElseIf cat.Equals("FWA", StringComparison.OrdinalIgnoreCase) Then
                    fwaClans.Add(Tuple.Create(name, formattedLine))
                Else
                    backupClans.Add(Tuple.Create(name, formattedLine))
                End If
            Next

            ' Sort lists by Clan Name to maintain clean alphabetized appearance
            cwlClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))
            fwaClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))
            backupClans.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))

            ' 3. Build the Discord layout output matching your design
            Dim embed As New EmbedBuilder() With {
            .Title = "PAK: 💎FWA💎 Clans",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }

            Dim description As New StringBuilder()

            ' Append CWL Section
            If cwlClans.Count > 0 Then
                description.AppendLine("**CWL**")
                For Each cwl In cwlClans
                    description.AppendLine(cwl.Item2)
                Next
                description.AppendLine() ' Spacer line
            End If

            ' Append FWA Section
            If fwaClans.Count > 0 Then
                description.AppendLine("**FWA**")
                For Each fwa In fwaClans
                    description.AppendLine(fwa.Item2)
                Next
                description.AppendLine() ' Spacer line
            End If

            ' Append CWL Backup Section (if any exist)
            If backupClans.Count > 0 Then
                description.AppendLine("**CWL Backup**")
                For Each backup In backupClans
                    description.AppendLine(backup.Item2)
                Next
            End If

            embed.WithDescription(description.ToString())
            embed.WithFooter(footer:=New EmbedFooterBuilder().WithText($"Total : {clans.Count} Clans"))

            ' =========================================================================
            ' INTERACTIVE REFRESH INTERACTION BUTTON GENERATION
            ' =========================================================================
            ' Build the physical action row framework button directly inside this transaction thread boundary
            Dim components = New ComponentBuilder().WithButton(
        label:="Refresh",
        customId:="refresh_clan_list",
        style:=ButtonStyle.Primary,
        emote:=New Emoji("🔄")
    ).Build()

            ' Return both elements grouped inside a Tuple wrapper
            Return Tuple.Create(embed.Build(), components)

        End Function
        ''' <summary>
        ''' Formats list output grids via /clan-list
        ''' </summary>
        Public Shared Async Function HandleClanListAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()

            ' Invoke the centralized core rendering engine
            Dim messageData = Await BuildClanListMessageAsync(command.GuildId.Value)

            If messageData IsNot Nothing Then
                Await command.ModifyOriginalResponseAsync(Sub(p)
                                                              p.Embed = messageData.Item1
                                                              p.Components = messageData.Item2
                                                          End Sub)
            Else
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No clans are registered to this server database. Use `/clan-add` to begin.")
            End If
        End Function
        ''' <summary>
        ''' Formats list output grids via /dump filtering clans where DUMP is '1'
        ''' </summary>
        Public Shared Async Function HandleDumpListAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()

            ' 1. Fetch tracked clans from Oracle DB
            Dim clans As List(Of Dictionary(Of String, String)) = Await OracleDatabaseManager.GetDumpClansAsync(command.GuildId.Value.ToString())

            If clans Is Nothing OrElse clans.Count = 0 Then
                Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No clans are marked for dump in this server database.")
                Return
            End If

            Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
            Dim description As New StringBuilder()

            ' 2. Build the list with correct URL formatting
            For Each clan In clans
                Dim tag As String = clan("tag")
                Dim name As String = clan("name")
                Dim memberCount As String = "N/A"

                ' Fetch live member count from API
                Dim clanData As Newtonsoft.Json.Linq.JObject = Await cocApi.GetClanDataAsync(tag)
                If clanData IsNot Nothing AndAlso clanData("members") IsNot Nothing Then
                    memberCount = clanData("members").ToString()
                End If

                ' FIX 1: Use the official Deep Link structure and escape the '#' properly
                Dim cleanTagForUrl As String = tag.Replace("#", "%23")
                Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTagForUrl}"

                ' FIX 2: Correct Discord Markdown formatting: • [Clan Name (#Tag)](URL) - Members
                description.AppendLine($"• [{name} ({tag})]({cocDeepLink}) - {memberCount}")
            Next

            ' 3. Build and send the final Embed
            Dim embed As New EmbedBuilder() With {
            .Title = "PAK: Registered Clans (Dump)",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }

            embed.WithDescription(description.ToString())
            embed.WithFooter(footer:=New EmbedFooterBuilder().WithText($"Total: {clans.Count} Clans"))

            Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
        End Function
        Public Shared Async Function HandleClCommandAsync(command As SocketSlashCommand) As Task
            Await command.DeferAsync()

            ' The value received here is the clan tag (e.g., #2JLJVYQPU) passed from Autocomplete
            Dim clanTag As String = command.Data.Options.First().Value.ToString().ToUpper().Trim()

            ' Open connection to check current stats if necessary
            Dim cocApi As New ClashOfClansAPI(CocService.apiToken)
            Dim clanData = Await cocApi.GetClanDataAsync(clanTag)

            Dim clanName As String = "Unknown Clan"
            Dim memberCount As String = "N/A"

            If clanData IsNot Nothing Then
                clanName = clanData("name")?.ToString()
                memberCount = clanData("members")?.ToString()
            End If

            ' Official working Supercell web link redirect
            Dim cleanTag As String = clanTag.Replace("#", "")
            Dim cocDeepLink As String = $"https://link.clashofclans.com/en/?action=OpenClanProfile&tag={cleanTag}"

            ' Build simple clean UI layout response
            Dim embed As New EmbedBuilder() With {
            .Title = $"🔗 Join Link: {clanName}",
            .Color = Color.Green,
            .Timestamp = DateTimeOffset.Now
        }

            embed.WithDescription($"Click the link below to view or join **{clanName}** inside the Clash of Clans app!{Environment.NewLine}{Environment.NewLine}" &
                              $"👉 **[Open Profile & Join ({clanTag})]({cocDeepLink})**")

            embed.AddField("Current Status", $"👥 **Members:** {memberCount}/50", inline:=True)
            embed.WithFooter(footer:=New EmbedFooterBuilder().WithText("`Pak Admin Bot System`"))

            Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
        End Function


        Public Shared Async Function HandleAutoCompleteClansAsync(autocomplete As SocketAutocompleteInteraction) As Task
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
        End Function

        Public Shared Async Function HandleClanlistUpdate(component As SocketMessageComponent) As Task
            Try
                Await component.DeferAsync()

                ' Invoke the identical central core engine to pull brand new live stats
                Dim messageData = Await ClanManager.BuildClanListMessageAsync(component.GuildId.Value)

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

        End Function
    End Class

End Module
