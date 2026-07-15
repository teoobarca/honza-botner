using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using HonzaBotner.Discord.EventHandler;
using HonzaBotner.Discord.Services.Options;
using HonzaBotner.Services.Contract;
using Microsoft.Extensions.Options;

namespace HonzaBotner.Discord.Services.EventHandlers;

public class RoleBindingsHandler : IEventHandler<MessageReactionAddEventArgs>,
    IEventHandler<MessageReactionRemoveEventArgs>
{
    private readonly IRoleBindingsService _roleBindingsService;
    private readonly CommonCommandOptions _options;

    public RoleBindingsHandler(IRoleBindingsService roleBindingsService, IOptions<CommonCommandOptions> options)
    {
        _roleBindingsService = roleBindingsService;
        _options = options.Value;
    }

    public async Task<EventHandlerResult> Handle(MessageReactionAddEventArgs eventArgs)
    {
        if (eventArgs.User.IsBot)
            return EventHandlerResult.Continue;

        ICollection<ulong> mappings =
            await _roleBindingsService.FindMappingAsync(eventArgs.Channel.Id, eventArgs.Message.Id,
                eventArgs.Emoji.Name);
        if (!mappings.Any())
            return EventHandlerResult.Continue;

        DiscordMember member = await eventArgs.Guild.GetMemberAsync(eventArgs.User.Id);

        foreach (ulong roleId in mappings)
        {
            DiscordRole? role = eventArgs.Guild.GetRole(roleId);
            if (role == null || !DiscordAuthorization.CanDelegateRole(
                    eventArgs.Guild.CurrentMember, eventArgs.Guild.CurrentMember, role,
                    _options.SelfAssignableRoleIds.Contains(role.Id)))
                continue;

            await member.GrantRoleAsync(role, "Add role from binding");
        }

        return EventHandlerResult.Stop;
    }

    public async Task<EventHandlerResult> Handle(MessageReactionRemoveEventArgs eventArgs)
    {
        if (eventArgs.User.IsBot)
            return EventHandlerResult.Continue;

        ICollection<ulong> mappings =
            await _roleBindingsService.FindMappingAsync(eventArgs.Channel.Id, eventArgs.Message.Id,
                eventArgs.Emoji.Name);
        if (!mappings.Any())
            return EventHandlerResult.Continue;

        DiscordMember member = await eventArgs.Guild.GetMemberAsync(eventArgs.User.Id);

        foreach (ulong roleId in mappings)
        {
            DiscordRole? role = eventArgs.Guild.GetRole(roleId);
            if (role == null || role.IsManaged || eventArgs.Guild.CurrentMember.Hierarchy <= role.Position)
                continue;

            await member.RevokeRoleAsync(role, "Remove role because of binding");
        }

        return EventHandlerResult.Stop;
    }
}
