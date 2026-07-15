using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HonzaBotner.Discord.Services.Options;
using HonzaBotner.Services.Contract;
using HonzaBotner.Services.Contract.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace HonzaBotner.Controllers;

[ApiController]
[Route("[controller]")]
[EnableRateLimiting("auth")]
public class AuthController : BaseController
{
    private const string StateCookieName = "honza-botner-oauth-state";
    private const string VerifierCookieName = "honza-botner-oauth-verifier";
    private const string RolesPoolCookieName = "honza-botner-roles-pool";
    private readonly ILogger<AuthController> _logger;
    private readonly IAuthorizationService _authorizationService;
    private readonly CvutConfig _cvutConfig;
    private string RedirectUri => _cvutConfig.AppBaseUrl + "/Auth/" + nameof(Callback);

    public AuthController(
        ILogger<AuthController> logger,
        IAuthorizationService authorizationService,
        IOptions<InfoOptions> options, IOptions<CvutConfig> cvutConfig) : base(options)
    {
        _logger = logger;
        _authorizationService = authorizationService;
        _cvutConfig = cvutConfig.Value;
    }

    [HttpGet("Authenticate/{pool}")]
    public async Task<ActionResult> Authenticate(string pool)
    {
        if (!GetRolesPool(pool, out _)) return BadRequest();

        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        CookieOptions cookieOptions = new()
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/Auth"
        };
        Response.Cookies.Append(StateCookieName, state, cookieOptions);
        Response.Cookies.Append(VerifierCookieName, verifier, cookieOptions);
        Response.Cookies.Append(RolesPoolCookieName, pool, cookieOptions);

        string uri = await _authorizationService.GetAuthLinkAsync(RedirectUri, state, challenge);
        return Redirect(uri);
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpGet(nameof(Callback))]
    public async Task<ActionResult> Callback()
    {
        if (!Request.Cookies.TryGetValue(StateCookieName, out string? expectedState)
            || !Request.Cookies.TryGetValue(VerifierCookieName, out string? codeVerifier)
            || !Request.Cookies.TryGetValue(RolesPoolCookieName, out string? pool)
            || !Request.Query.TryGetValue("state", out StringValues states)
            || !Request.Query.TryGetValue("code", out StringValues codes))
        {
            DeleteAuthCookies();
            return BadRequest();
        }

        string? code = codes.Any() ? codes[0] : null;
        string? state = states.Any() ? states[0] : null;
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) ||
            !FixedTimeEquals(expectedState, state) ||
            !GetRolesPool(pool, out RolesPool rolesPool))
        {
            DeleteAuthCookies();
            return BadRequest();
        }

        try
        {
            string accessToken = await _authorizationService.GetAccessTokenAsync(code, RedirectUri, codeVerifier);
            string userName = await _authorizationService.GetUserNameAsync(accessToken);
            string completionCode = await _authorizationService.PrepareAuthorizationAsync(accessToken, userName,
                rolesPool);

            return Page($"Return to Discord and run /verification complete code:{completionCode}. " +
                        "The code expires in 10 minutes and works only once.", 200);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Couldn't prepare authentication completion");
            return Page("Authentication could not be completed. Please start again from Discord.", 500);
        }
        finally
        {
            DeleteAuthCookies();
        }
    }

    private bool GetRolesPool(string? value, out RolesPool rolesPool)
    {
        switch (value?.ToLowerInvariant())
        {
            case "auth":
                rolesPool = RolesPool.Auth;
                break;
            case "staff":
                rolesPool = RolesPool.Staff;
                break;
            default:
                rolesPool = RolesPool.Auth;
                return false;
        }

        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private void DeleteAuthCookies()
    {
        CookieOptions options = new() { Path = "/Auth", Secure = true, SameSite = SameSiteMode.Lax };
        Response.Cookies.Delete(StateCookieName, options);
        Response.Cookies.Delete(VerifierCookieName, options);
        Response.Cookies.Delete(RolesPoolCookieName, options);
    }
}
