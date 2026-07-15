using System.Linq;
using DSharpPlus;
using DSharpPlus.Entities;

namespace HonzaBotner.Discord.Services;

internal static class DiscordAuthorization
{
    private const Permissions PrivilegedRolePermissions =
        Permissions.Administrator |
        Permissions.ManageRoles |
        Permissions.ManageGuild |
        Permissions.ManageChannels |
        Permissions.ManageMessages |
        Permissions.ManageWebhooks |
        Permissions.ManageEmojis |
        Permissions.ManageEvents |
        Permissions.ManageThreads |
        Permissions.ManageNicknames |
        Permissions.ViewAuditLog |
        Permissions.MentionEveryone |
        Permissions.BanMembers |
        Permissions.KickMembers |
        Permissions.MuteMembers |
        Permissions.DeafenMembers |
        Permissions.MoveMembers |
        Permissions.ModerateMembers;

    public static bool HasChannelPermissions(DiscordMember member, DiscordChannel channel,
        params Permissions[] required) =>
        required.All(permission => channel.PermissionsFor(member).HasPermission(permission));

    public static bool CanDelegateRole(DiscordMember caller, DiscordMember bot, DiscordRole role,
        bool explicitlySelfAssignable = true) =>
        explicitlySelfAssignable &&
        !role.IsManaged &&
        caller.Hierarchy > role.Position &&
        bot.Hierarchy > role.Position &&
        !role.Permissions.HasPermission(Permissions.Administrator) &&
        (role.Permissions & PrivilegedRolePermissions) == Permissions.None;
}
