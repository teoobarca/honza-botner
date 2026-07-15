using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using HonzaBotner.Discord.Services;
using HonzaBotner.Discord.Services.Options;
using HonzaBotner.Services.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HonzaBotner.Discord.Services.Commands;

[SlashCommandGroup("message", "Commands to interact with messages.")]
[SlashCommandPermissions(Permissions.ManageMessages)]
[SlashRequirePermissions(Permissions.ManageMessages)]
[SlashModuleLifespan(SlashModuleLifespan.Scoped)]
public class MessageCommands : ApplicationCommandModule
{
    private readonly ILogger<MessageCommands> _logger;
    private readonly IRoleBindingsService _roleBindingsService;
    private readonly CommonCommandOptions _options;

    public MessageCommands(ILogger<MessageCommands> logger, IRoleBindingsService roleBindingsService,
        IOptions<CommonCommandOptions> options)
    {
        _logger = logger;
        _roleBindingsService = roleBindingsService;
        _options = options.Value;
    }

    [SlashCommand("send", "Sends a text message to the specified channel.")]
    public async Task SendMessageCommandAsync(
        InteractionContext ctx,
        [Option("channel", "Target channel for the message")] DiscordChannel channel,
        [Option("new-message", "Link to the message with content you want sent")] string link,
        [Option("mention", "Should the message include mentions? Default: false")] bool mention = false)
    {
        DiscordMessage? messageToSend = await DiscordHelper.FindMessageFromLink(ctx.Guild, link);

        if (messageToSend is null)
        {
            await ctx.CreateResponseAsync("Could not find linked message, does the bot have access to that channel?");
            return;
        }

        if (!DiscordAuthorization.HasChannelPermissions(ctx.Member, messageToSend.Channel,
                Permissions.AccessChannels, Permissions.ReadMessageHistory) ||
            !DiscordAuthorization.HasChannelPermissions(ctx.Member, channel,
                Permissions.AccessChannels, Permissions.SendMessages))
        {
            await ctx.CreateResponseAsync("You do not have access to the source or target channel.", true);
            return;
        }

        if (mention && !ctx.Member.PermissionsIn(channel).HasPermission(Permissions.MentionEveryone))
        {
            await ctx.CreateResponseAsync("You cannot relay mass mentions in the target channel.", true);
            return;
        }

        try
        {
            var content = new DiscordMessageBuilder()
                .WithContent(messageToSend.Content)
                .WithAllowedMentions(mention ? Mentions.All : Mentions.None);

            DiscordMessage snt = await channel.SendMessageAsync(content);
            await ctx.CreateResponseAsync("Message sent\n" + snt.JumpLink);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error during sending bot message");
            await ctx.CreateResponseAsync("Error occured during message send, see log for more information");
        }
    }

    [SlashCommand("edit", "Edit previously sent text message authored by this bot.")]
    public async Task EditMessageCommandAsync(
        InteractionContext ctx,
        [Option("old-message", "Link to the message you want to edit")] string originalUrl,
        [Option("new-message", "Link to a message with new content")] string newUrl,
        [Option("mention", "Should all mentions be included? Default: false")] bool mention = false)
    {
        DiscordMessage? oldMessage = await DiscordHelper.FindMessageFromLink(ctx.Guild, originalUrl);
        DiscordMessage? newMessage = await DiscordHelper.FindMessageFromLink(ctx.Guild, newUrl);

        if (oldMessage is null || newMessage is null)
        {
            await ctx.CreateResponseAsync("Could not resolve one of the provided messages");
            return;
        }

        if (!DiscordAuthorization.HasChannelPermissions(ctx.Member, newMessage.Channel,
                Permissions.AccessChannels, Permissions.ReadMessageHistory) ||
            !DiscordAuthorization.HasChannelPermissions(ctx.Member, oldMessage.Channel,
                Permissions.AccessChannels, Permissions.ManageMessages))
        {
            await ctx.CreateResponseAsync("You do not have access to the source or target message.", true);
            return;
        }

        if (mention && !ctx.Member.PermissionsIn(oldMessage.Channel).HasPermission(Permissions.MentionEveryone))
        {
            await ctx.CreateResponseAsync("You cannot relay mass mentions in the target channel.", true);
            return;
        }

        if (!oldMessage.Author.IsCurrent)
        {
            await ctx.CreateResponseAsync("Can not edit messages which were not sent by this bot (duh)");
            return;
        }

        var content = new DiscordMessageBuilder()
            .WithContent(newMessage.Content)
            .WithAllowedMentions(mention ? Mentions.All : Mentions.None);

        try
        {
            DiscordMessage edited = await oldMessage.ModifyAsync(content);
            await ctx.CreateResponseAsync("Message successfully edited.\n" + edited.JumpLink);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not edit message in {OldMessageChannel}", oldMessage.Channel.Name);
            await ctx.CreateResponseAsync("Error occured during message edit, see log for more information");
        }
    }

    [SlashCommand("react", "Reacts to a message as this bot.")]
    public async Task ReactToMessageCommandAsync(
        InteractionContext ctx,
        [Option("message", "Link to the message")] string url
        )
    {
        DiscordGuild guild = ctx.Guild;
        DiscordMessage? oldMessage = await DiscordHelper.FindMessageFromLink(guild, url);

        if (oldMessage is null)
        {
            await ctx.CreateResponseAsync("Could not find message to react to.");
            return;
        }

        if (!DiscordAuthorization.HasChannelPermissions(ctx.Member, oldMessage.Channel,
                Permissions.AccessChannels, Permissions.ReadMessageHistory, Permissions.ManageMessages))
        {
            await ctx.CreateResponseAsync("You cannot manage messages in the target channel.", true);
            return;
        }

        await ctx.CreateResponseAsync("React to this message with reactions you want to add");
        var reactionCatch = await ctx.GetOriginalResponseAsync();
        var interactivity = ctx.Client.GetInteractivity();
        var response = await interactivity
            .WaitForReactionAsync(reactionCatch, ctx.User, TimeSpan.FromMinutes(2));

        while (!response.TimedOut)
        {
            try
            {
                await oldMessage.CreateReactionAsync(response.Result.Emoji);
                await ctx.EditResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent("Reacted with " + response.Result.Emoji + "\nReact with more to add more"));
            }
            catch (BadRequestException)
            {
                await ctx.EditResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent("Bot cannot react with provided emoji. Is it universal/from this server?"));
            }
            catch (UnauthorizedException)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Too many reactions"));
                return;
            }

            response = await interactivity.WaitForReactionAsync(reactionCatch, ctx.User, TimeSpan.FromMinutes(2));
        }

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("No more reactions added"));
    }

    [SlashCommand("bind", "Bind or unbind roles to specified message and reaction")]
    public async Task BindCommandAsync(
        InteractionContext ctx,
        [Option("message", "Link to modified message")] string url,
        [Option("roles", "Mention roles you want to (un)bind")] string roles,
        [Choice("add", "add")]
        [Choice("remove", "remove")]
        [Option("action", "Add new binding or remove existing?")] string action)
    {
        DiscordMessage? message = await DiscordHelper.FindMessageFromLink(ctx.Guild, url);
        if (message == null)
        {
            await ctx.CreateResponseAsync("Unable to find message with provided link", true);
            return;
        }

        if (!DiscordAuthorization.HasChannelPermissions(ctx.Member, message.Channel,
                Permissions.AccessChannels, Permissions.ReadMessageHistory, Permissions.ManageMessages) ||
            !ctx.Member.Permissions.HasPermission(Permissions.ManageRoles))
        {
            await ctx.CreateResponseAsync("Managing role bindings requires Manage Roles and access to the target channel.", true);
            return;
        }

        DiscordRole[] resolvedRoles = ctx.ResolvedRoleMentions.ToArray();
        if (resolvedRoles.Length == 0 || resolvedRoles.Any(role =>
                !DiscordAuthorization.CanDelegateRole(ctx.Member, ctx.Guild.CurrentMember, role,
                    _options.SelfAssignableRoleIds.Contains(role.Id))))
        {
            await ctx.CreateResponseAsync(
                "Every role must be explicitly allowlisted for self-assignment, unmanaged, non-privileged, " +
                "and below both your and the bot's highest role.", true);
            return;
        }

        ulong channelId = message.ChannelId;
        ulong messageId = message.Id;

        await ctx.CreateResponseAsync("React to this message with emoji you want to (un)bind");
        var interactivity = ctx.Client.GetInteractivity();
        DiscordMessage responseMessage = await ctx.GetOriginalResponseAsync();
        var response = await interactivity.WaitForReactionAsync(responseMessage, ctx.User,
            TimeSpan.FromMinutes(2));

        if (response.TimedOut)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Timed out, write command again"));
            return;
        }

        try
        {
            switch (action)
            {
                case "add":
                    await message.CreateReactionAsync(response.Result.Emoji);
                    await _roleBindingsService.AddBindingsAsync(channelId, messageId, response.Result.Emoji.Name,
                        resolvedRoles.Select(r => r.Id).ToHashSet());
                    break;
                case "remove":
                    bool someRemained = await _roleBindingsService.RemoveBindingsAsync(channelId, messageId,
                        response.Result.Emoji.Name,
                        resolvedRoles.Select(r => r.Id).ToHashSet());
                    if (!someRemained) await message.DeleteReactionsEmojiAsync(response.Result.Emoji);
                    break;
            }

            await ctx.FollowUpAsync(
                new DiscordFollowupMessageBuilder().WithContent("Successfully " + action + "ed role binding"));
        }
        catch (BadRequestException)
        {
            await ctx.FollowUpAsync(
                new DiscordFollowupMessageBuilder().WithContent("Cannot use provided emote."));
        }
        catch (Exception e)
        {
            await ctx.FollowUpAsync(
                new DiscordFollowupMessageBuilder().WithContent("Error occured. Refer to log for more info."));
            _logger.LogError(e, "Error occured during command {Action} bind of roles to message", action);
        }
    }
}
