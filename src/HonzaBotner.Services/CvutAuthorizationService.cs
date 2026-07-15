using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HonzaBotner.Database;
using HonzaBotner.Services.Contract;
using HonzaBotner.Services.Contract.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HonzaBotner.Services;

public class CvutAuthorizationService : IAuthorizationService
{
    private readonly HonzaBotnerDbContext _dbContext;
    private readonly CvutConfig _cvutConfig;
    private readonly IUsermapInfoService _usermapInfoService;
    private readonly IDiscordRoleManager _roleManager;
    private readonly HttpClient _client;
    private readonly IHashService _hashService;
    private readonly ILogger<CvutAuthorizationService> _logger;
    private readonly IVerificationChallengeStore _challengeStore;

    public CvutAuthorizationService(HonzaBotnerDbContext dbContext, IOptions<CvutConfig> cvutConfig,
        IUsermapInfoService usermapInfoService, IDiscordRoleManager roleManager, HttpClient client,
        IHashService hashService, ILogger<CvutAuthorizationService> logger,
        IVerificationChallengeStore challengeStore)
    {
        _dbContext = dbContext;
        _cvutConfig = cvutConfig.Value;
        _usermapInfoService = usermapInfoService;
        _roleManager = roleManager;
        _client = client;
        _hashService = hashService;
        _logger = logger;
        _challengeStore = challengeStore;
    }

    public async Task<string> PrepareAuthorizationAsync(
        string accessToken,
        string username,
        RolesPool rolesPool
    )
    {
        UsermapPerson? person = await _usermapInfoService.GetUserInfoAsync(accessToken, username);
        if (person == null)
        {
            _logger.LogWarning("Couldn't fetch info from UserMap");
            throw new InvalidOperationException("Couldn't fetch verification data from UserMap.");
        }

        string authId = _hashService.Hash(person.Username);
        string legacyAuthId = _hashService.LegacyHash(person.Username);
        IReadOnlySet<ulong> roleIds = _roleManager.MapUsermapRoles(person.Roles, rolesPool)
            .Select(role => role.RoleId)
            .ToHashSet();

        return _challengeStore.Create(authId, legacyAuthId, rolesPool, roleIds);
    }

    public async Task<IAuthorizationService.AuthorizeResult> CompleteAuthorizationAsync(string code, ulong userId)
    {
        if (!_challengeStore.TryTake(code, out VerificationChallenge? challenge) || challenge is null)
            return IAuthorizationService.AuthorizeResult.Failed;

        RolesPool rolesPool = challenge.RolesPool;
        string authId = challenge.AuthId;
        string legacyAuthId = challenge.LegacyAuthId;
        HashSet<DiscordRole> discordRoles = challenge.RoleIds.Select(id => new DiscordRole(id)).ToHashSet();

        if (rolesPool != RolesPool.Auth)
        {
            Verification? verification = await _dbContext.Verifications.FindAsync(userId);
            if ((verification?.AuthId != authId && verification?.AuthId != legacyAuthId) ||
                !await _roleManager.IsUserDiscordAuthenticated(userId))
                return verification is null
                    ? IAuthorizationService.AuthorizeResult.AuthorizeFirst
                    : IAuthorizationService.AuthorizeResult.DifferentMember;
        }

        bool discordIdPresent = await IsUserVerified(userId);
        bool authPresent = await _dbContext.Verifications.AnyAsync(v =>
            v.AuthId == authId || v.AuthId == legacyAuthId);

        // discord and auth -> update roles
        if (discordIdPresent && authPresent)
        {
            bool verificationExists =
                await _dbContext.Verifications.AnyAsync(v => v.UserId == userId &&
                                                            (v.AuthId == authId || v.AuthId == legacyAuthId));

            if (verificationExists)
            {
                Verification verification = await _dbContext.Verifications.FindAsync(userId)
                    ?? throw new InvalidOperationException("Verification disappeared during update.");
                if (verification.AuthId == legacyAuthId)
                {
                    verification.AuthId = authId;
                    await _dbContext.SaveChangesAsync();
                }
                bool revoked = await _roleManager.RevokeRolesPoolAsync(userId, rolesPool);
                if (!revoked)
                {
                    _logger.LogWarning("Revoking roles pool {RolesPool} for user id {UserId} failed", rolesPool,
                        userId);
                    return IAuthorizationService.AuthorizeResult.Failed;
                }

                bool granted = await _roleManager.GrantRolesAsync(userId, discordRoles);
                if (!granted) return IAuthorizationService.AuthorizeResult.Failed;

                if (rolesPool == RolesPool.Staff) verification.StaffVerifiedAt = DateTime.UtcNow;
                else verification.LastVerifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return IAuthorizationService.AuthorizeResult.OK;
            }

            return IAuthorizationService.AuthorizeResult.DifferentMember;
        }

        // discord xor auth -> user already verified, error
        if (discordIdPresent || authPresent)
        {
            return IAuthorizationService.AuthorizeResult.DifferentMember;
        }

        // nothing -> create database entry, update roles
        {
            Verification verification = new()
            {
                AuthId = authId,
                UserId = userId,
                LastVerifiedAt = DateTime.UtcNow
            };
            await _dbContext.Verifications.AddAsync(verification);
            await _dbContext.SaveChangesAsync();

            if (await _roleManager.GrantRolesAsync(userId, discordRoles))
            {
                await _roleManager.RevokeHostRolesAsync(userId);
                return IAuthorizationService.AuthorizeResult.OK;
            }

            _dbContext.Verifications.Remove(verification);
            await _dbContext.SaveChangesAsync();
            return IAuthorizationService.AuthorizeResult.Failed;
        }
    }

    public Task<string> GetAuthLinkAsync(string redirectUri, string state, string codeChallenge)
    {
        if (string.IsNullOrEmpty(_cvutConfig.ClientId))
        {
            throw new ArgumentNullException(null, "Invalid config");
        }

        NameValueCollection parameters = new()
        {
            { "response_type", "code" },
            { "client_id", _cvutConfig.ClientId },
            { "redirect_uri", redirectUri },
            { "state", state },
            { "code_challenge", codeChallenge },
            { "code_challenge_method", "S256" }
        };
        UriBuilder builder = new("https://auth.fit.cvut.cz/oauth/authorize")
        {
            Query = parameters.GetQueryString()
        };
        return Task.FromResult(builder.Uri.ToString());
    }

    public async Task<bool> IsUserVerified(ulong userId)
    {
        return await _dbContext.Verifications
            .AnyAsync(v => v.UserId == userId);
    }

    public async Task<string> GetAccessTokenAsync(string code, string redirectUri, string codeVerifier)
    {
        const string tokenUri = "https://auth.fit.cvut.cz/oauth/token";

        string credentials =
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_cvutConfig.ClientId + ":" + _cvutConfig.ClientSecret));
        List<KeyValuePair<string?, string?>> formValues = new()
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("code_verifier", codeVerifier)
        };

        using HttpRequestMessage requestMessage = new()
        {
            RequestUri = new Uri(tokenUri),
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", credentials) },
            Method = HttpMethod.Post,
            Content = new FormUrlEncodedContent(formValues)
        };

        using HttpResponseMessage tokenResponse = await _client.SendAsync(requestMessage,
            HttpCompletionOption.ResponseHeadersRead);

        try
        {
            tokenResponse.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Couldn't authorize user, status code is not successful.", e);
        }

        using JsonDocument response = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());

        return response.RootElement.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("Couldn't authorize user.");
    }

    public async Task<string> GetUserNameAsync(string accessToken)
    {
        const string checkTokenUri = "https://auth.fit.cvut.cz/oauth/check_token";

        using HttpRequestMessage request = new(HttpMethod.Post, checkTokenUri)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string?, string?>("token", accessToken)
            })
        };

        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string responseText = await response.Content.ReadAsStringAsync();
        using JsonDocument user = JsonDocument.Parse(responseText);

        return user.RootElement.GetProperty("user_name").GetString()
               ?? throw new InvalidOperationException("Couldn't load information about user");
    }

    public async Task<string> GetServiceTokenAsync(string scope)
    {
        const string tokenUri = "https://auth.fit.cvut.cz/oauth/oauth/token";

        // TOOD(ostorc): add cache keyed by scope, with lifetime based on response

        UriBuilder uriBuilder = new(tokenUri);

        List<KeyValuePair<string?, string?>> contentValues = new()
        {
            new("grant_type", "client_credentials"),
            new("client_id", _cvutConfig.ServiceId),
            new("client_secret", _cvutConfig.ServiceSecret),
            new("scope", scope)
        };

        using FormUrlEncodedContent content = new(contentValues);

        using HttpRequestMessage requestMessage = new()
        {
            RequestUri = uriBuilder.Uri,
            Method = HttpMethod.Post,
            Content = content
        };

        using HttpResponseMessage tokenResponse = await _client.SendAsync(requestMessage,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();

        using JsonDocument response =
            await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));

        return response.RootElement.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("Couldn't get service token from CTU");
    }
}
