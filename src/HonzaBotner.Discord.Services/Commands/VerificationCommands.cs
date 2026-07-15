using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using HonzaBotner.Services.Contract;

namespace HonzaBotner.Discord.Services.Commands;

[SlashCommandGroup("verification", "Complete account verification securely.")]
[SlashModuleLifespan(SlashModuleLifespan.Scoped)]
public sealed class VerificationCommands : ApplicationCommandModule
{
    private readonly IAuthorizationService _authorizationService;

    public VerificationCommands(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    [SlashCommand("complete", "Complete CTU verification using the one-time code shown in your browser.")]
    public async Task CompleteAsync(
        InteractionContext context,
        [Option("code", "One-time verification code")]
        string code)
    {
        await context.DeferAsync(true);
        IAuthorizationService.AuthorizeResult result =
            await _authorizationService.CompleteAuthorizationAsync(code, context.User.Id);

        string message = result switch
        {
            IAuthorizationService.AuthorizeResult.OK => "Verification completed successfully.",
            IAuthorizationService.AuthorizeResult.DifferentMember =>
                "This CTU identity is already linked to another Discord account.",
            IAuthorizationService.AuthorizeResult.AuthorizeFirst =>
                "Complete regular verification before requesting staff roles.",
            _ => "The code is invalid, expired, already used, or the role update failed. Start verification again."
        };

        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
    }
}
