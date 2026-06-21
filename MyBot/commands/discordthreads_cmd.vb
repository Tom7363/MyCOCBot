Imports Discord
Imports Discord.Rest
Imports Discord.WebSocket

Public Module DiscordThreadsCmd
    Public Class DiscordThreads

        Public Shared Async Function RegisterThreadCommandAsync(client As DiscordSocketClient, guild As SocketGuild) As Task
            ' /threadembed
            Return 'Disabled for now 
            Dim threadEmbedCmd = New SlashCommandBuilder() With {
                                     .Name = "threadembed",
                                     .Description = "Displays an overview of all active threads (Requires Server Orga)"
                                 }

            ' /deletethread
            Dim deleteThreadCmd = New SlashCommandBuilder() With {
                                     .Name = "deletethread",
                                     .Description = "Permanently deletes a thread via its ID (Requires Server Orga)"
                                 }.AddOption("id", ApplicationCommandOptionType.String, "The exact ID of the thread to delete", isRequired:=True)

            ' /movetothread
            Dim moveThreadCmd = New SlashCommandBuilder() With {
                                     .Name = "movetothread",
                                     .Description = "Moves a message from the channel into a specific thread (Requires Server Orga)"
                                 }.AddOption("message_id", ApplicationCommandOptionType.String, "The ID of the message to move", isRequired:=True) _
                                  .AddOption("thread_id", ApplicationCommandOptionType.String, "The ID of the target thread", isRequired:=True)
            Await guild.CreateApplicationCommandAsync(threadEmbedCmd.Build())
            Await guild.CreateApplicationCommandAsync(deleteThreadCmd.Build())
            Await guild.CreateApplicationCommandAsync(moveThreadCmd.Build())


        End Function

        Public Shared Async Function HandleThreadEmbedCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            Dim gUser = TryCast(command.User, SocketGuildUser)
            If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
                Dim txtChannel = TryCast(command.Channel, SocketTextChannel)
                If txtChannel IsNot Nothing Then
                    Dim aktiveThreads = Await txtChannel.GetActiveThreadsAsync()

                    Dim embedBuilder As New EmbedBuilder() With {
                                         .Title = $"📂 Thread Overview for #{txtChannel.Name}",
                                         .Description = "Here are the currently active discussion threads in this channel:",
                                         .Color = New Color(52, 152, 219)
                                     }
                    embedBuilder.WithCurrentTimestamp()

                    If aktiveThreads.Count = 0 Then
                        embedBuilder.Description = "There are currently no active threads in this channel. ❌"
                    Else
                        For Each thread As RestThreadChannel In aktiveThreads
                            Dim feldInhalt As String = $"• **Created by:** <@{thread.OwnerId}>{Environment.NewLine}" &
                                                                        $"• **Messages:** `{thread.MessageCount}`{Environment.NewLine}" &
                                                                        $"• **Members:** `{thread.MemberCount}`{Environment.NewLine}" &
                                                                        $"• **Link to Thread:** <#{thread.Id}>"

                            embedBuilder.AddField($"🧵 #{thread.Name}", feldInhalt, inline:=False)
                        Next
                    End If

                    embedBuilder.WithFooter("`Pak Admin Bot System`")
                    Await command.RespondAsync(embed:=embedBuilder.Build())
                End If
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If
        End Function

        Public Shared Async Function HandleDeleteThreadCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            Dim gUser = TryCast(command.User, SocketGuildUser)
            If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
                Dim idStr As String = TryCast(command.Data.Options.First().Value, String)
                Dim threadId As ULong

                If ULong.TryParse(idStr, threadId) Then
                    Dim thread = TryCast(client.GetChannel(threadId), SocketThreadChannel)
                    If thread IsNot Nothing Then
                        Dim name As String = thread.Name
                        Await thread.DeleteAsync()
                        Await command.RespondAsync($"The thread **#{name}** was successfully deleted! 🗑️")
                    Else
                        Await command.RespondAsync("The thread could not be found.", ephemeral:=True)
                    End If
                Else
                    Await command.RespondAsync("Invalid Thread ID format.", ephemeral:=True)
                End If
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If

        End Function

        Public Shared Async Function HandleMoveToThreadCommandAsync(command As SocketSlashCommand, client As DiscordSocketClient) As Task
            Dim gUser = TryCast(command.User, SocketGuildUser)
            If gUser IsNot Nothing AndAlso gUser.Roles.Any(Function(r) r.Name = "Server Orga") Then
                Dim msgIdStr As String = TryCast(command.Data.Options.Where(Function(o) o.Name = "message_id").First().Value, String)
                Dim threadIdStr As String = TryCast(command.Data.Options.Where(Function(o) o.Name = "thread_id").First().Value, String)

                Dim msgId, threadId As ULong
                If ULong.TryParse(msgIdStr, msgId) AndAlso ULong.TryParse(threadIdStr, threadId) Then
                    Dim thread = TryCast(client.GetChannel(threadId), SocketThreadChannel)
                    Dim originalMsg = Await command.Channel.GetMessageAsync(msgId)

                    If thread IsNot Nothing AndAlso originalMsg IsNot Nothing Then
                        Dim embedBuilder As New EmbedBuilder() With {
                                             .Author = New EmbedAuthorBuilder() With {.Name = originalMsg.Author.Username, .IconUrl = originalMsg.Author.GetAvatarUrl()},
                                             .Description = originalMsg.Content,
                                             .Color = New Color(230, 126, 34),
                                             .Timestamp = originalMsg.Timestamp
                                         }

                        Await thread.SendMessageAsync(text:="*Moved Message:*", embed:=embedBuilder.Build())
                        Await originalMsg.DeleteAsync()

                        Await command.RespondAsync("Message successfully moved! 📦")
                    Else
                        Await command.RespondAsync("Message or Thread could not be found.", ephemeral:=True)
                    End If
                Else
                    Await command.RespondAsync("Invalid ID format parsed.", ephemeral:=True)
                End If
            Else
                Await command.RespondAsync("❌ You do not have permission to use this command! Required role: **Server Orga**", ephemeral:=True)
            End If
        End Function

    End Class

End Module