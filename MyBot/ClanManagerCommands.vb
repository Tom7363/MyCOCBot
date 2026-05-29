Imports System
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Discord
Imports Discord.WebSocket
Imports Newtonsoft.Json.Linq

Public Module ClanManagerCommands

    ''' <summary>
    ''' Evaluates and saves an added clan via /clan-add [tag] [category]
    ''' </summary>
    Public Async Function HandleClanAddAsync(command As SocketSlashCommand) As Task
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
    Public Async Function HandleClanRemoveAsync(command As SocketSlashCommand) As Task
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
    ''' Formats list output grids via /clan-list
    ''' </summary>
    Public Async Function HandleClanListAsync(command As SocketSlashCommand) As Task
        ' Defer response since we are making multiple database and API calls
        Await command.DeferAsync()

        ' 1. Fetch tracked clans from Oracle DB
        Dim clans = Await OracleDatabaseManager.GetClansAsync(command.GuildId.Value)

        If clans.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No clans are registered to this server database. Use `/clan-add` to begin.")
            Return
        End If

        ' Lists to separate types dynamically
        Dim cwlClans As New List(Of Tuple(Of String, String))()
        Dim fwaClans As New List(Of Tuple(Of String, String))()
        Dim backupClans As New List(Of Tuple(Of String, String))()

        ' Instantiate API engine to bypass non-shared error if Option 2 was used
        Dim apiEngine As New API_COC()

        ' 2. Categorize and fetch live member counts from Supercell API
        For Each clan In clans
            Dim tag As String = clan.Item1
            Dim name As String = clan.Item2
            Dim cat As String = clan.Item3

            Dim memberCount As String = "N/A"

            Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

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

        ' Send final formatted layout back to Discord
        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function

    ''' <summary>
    ''' Formats list output grids via /clan-list filtering clans where DUMP is '1' without category separation
    ''' </summary>
    Public Async Function HandleDumpListAsync(command As SocketSlashCommand) As Task
        ' Defer response since we are making multiple database and API calls
        Await command.DeferAsync()

        ' 1. Fetch tracked clans from Oracle DB
        Dim clans As List(Of Dictionary(Of String, String)) = Await OracleDatabaseManager.GetDumpClansAsync(command.GuildId.Value)

        If clans Is Nothing OrElse clans.Count = 0 Then
            Await command.ModifyOriginalResponseAsync(Sub(p) p.Content = "ℹ️ No dump clans (DUMP = 1) are registered to this server database. Use `/clan-add` to begin.")
            Return
        End If

        ' Single list to collect all lines along with the clan name for sorting
        Dim allClansList As New List(Of Tuple(Of String, String))()

        ' Instantiate the API engine once before the loop to save resources
        Dim cocApi As New ClashOfClansAPI(CocService.apiToken)

        ' 2. Fetch live member counts from Supercell API and generate deep links
        For Each clan In clans
            ' FIX: Zugriff über Dictionary-Keys anstelle von Tuple .ItemX
            Dim tag As String = clan("tag")
            Dim name As String = clan("name")

            Dim memberCount As String = "N/A"

            Try
                ' Fetch live member count from API
                Dim clanData As JObject = Await cocApi.GetClanDataAsync(tag)
                If clanData IsNot Nothing AndAlso clanData("members") IsNot Nothing Then
                    memberCount = clanData("members").ToString()
                End If
            Catch ex As Exception
                Console.WriteLine($"[API ERROR] Failed to fetch data for {tag}: {ex.Message}")
            End Try

            ' FIX: Offizieller, voll funktionsfähiger Deep Link für die CoC App
            Dim cleanTagForUrl As String = tag.Replace("#", "%23")
            Dim cocDeepLink As String = $"https://clashofclans.com{cleanTagForUrl}"

            ' Format: • [Name (Tag)](DeepLink) - Members (Roher_DeepLink)
            Dim formattedLine As String = $"• [{name} ({tag})]({cocDeepLink}) - {memberCount} ({cocDeepLink})"

            ' Store name for alphabetical sorting, and the formatted string for output
            allClansList.Add(Tuple.Create(name, formattedLine))
        Next

        ' Sort the entire list alphabetically by Clan Name
        allClansList.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))

        ' 3. Build the flat Discord layout output
        Dim embed As New EmbedBuilder() With {
            .Title = "PAK: Registered Clans",
            .Color = Color.Blue,
            .Timestamp = DateTimeOffset.Now
        }

        Dim description As New StringBuilder()

        ' Append all sorted clans directly without subheaders
        For Each clanEntry In allClansList
            description.AppendLine(clanEntry.Item2)
        Next

        embed.WithDescription(description.ToString())
        embed.WithFooter(footer:=New EmbedFooterBuilder().WithText($"Total: {clans.Count} Clans"))

        ' Send final formatted layout back to Discord
        Await command.ModifyOriginalResponseAsync(Sub(p) p.Embed = embed.Build())
    End Function

    Public Async Function HandleClCommandAsync(command As SocketSlashCommand) As Task
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
        Dim cocDeepLink As String = $"https://clashofclans.com{cleanTag}"

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
End Module
