using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace HonzaBotner.Discord.Extensions;

public static class DiscordExtensions
{
    public static async Task ReportException(this DiscordChannel channel, string source, Exception exception)
    {
        await channel.SendMessageAsync(
            new DiscordEmbedBuilder()
                .WithTitle($"{source} - {exception.GetType().Name}")
                .WithColor(DiscordColor.Red)
                .WithTimestamp(DateTime.UtcNow)
                .WithDescription(
                    "Please react to this message to indicate that it is already logged in isssue or solved"
                )
                .Build()
        );
    }
}
