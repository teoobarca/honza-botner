using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using HonzaBotner.Discord;
using HonzaBotner.Discord.Extensions;
using HonzaBotner.Discord.Services.Options;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HonzaBotner.Controllers;

[ApiController]
public class ErrorController : BaseController
{
    private readonly IGuildProvider _guildProvider;
    private readonly IOptions<DiscordConfig> _discordOptions;
    private readonly ILogger<ErrorController> _logger;

    public ErrorController(
        IGuildProvider guildProvider,
        IOptions<DiscordConfig> options,
        IOptions<InfoOptions> infoOptions,
        ILogger<ErrorController> logger
    ) : base(infoOptions)
    {
        _guildProvider = guildProvider;
        _discordOptions = options;
        _logger = logger;
    }

    [Route("/error")]
    public async Task<IActionResult> Index()
    {
        IExceptionHandlerFeature? context = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (context?.Error is null)
            return Page("Something went wrong. Please contact @mod at server.", 500);

        _logger.LogError(context.Error, "Unhandled ASP.NET Core exception");

        try
        {
            DiscordGuild guild = await _guildProvider.GetCurrentGuildAsync();
            ulong logChannelId = _discordOptions.Value.LogChannelId;

            if (logChannelId != default)
            {
                DiscordChannel channel = guild.GetChannel(logChannelId);
                await channel.ReportException("ASP Core .NET", context.Error);
            }
        }
        catch (Exception notificationError)
        {
            _logger.LogError(notificationError, "Reporting the ASP.NET Core exception to Discord failed");
        }

        return Page("Something went wrong. Please contact @mod at server.", 500);
    }
}
