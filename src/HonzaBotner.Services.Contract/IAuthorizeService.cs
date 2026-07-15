using System.Threading.Tasks;
using HonzaBotner.Services.Contract.Dto;

namespace HonzaBotner.Services.Contract;

public interface IAuthorizationService
{
    public enum AuthorizeResult
    {
        OK,
        Failed,
        DifferentMember,
        UserMapError,
        AuthorizeFirst
    }

    Task<string> PrepareAuthorizationAsync(string accessToken, string username, RolesPool rolesPool);

    Task<AuthorizeResult> CompleteAuthorizationAsync(string code, ulong userId);

    Task<string> GetAuthLinkAsync(string redirectUri, string state, string codeChallenge);

    Task<bool> IsUserVerified(ulong userId);

    Task<string> GetAccessTokenAsync(string code, string redirectUri, string codeVerifier);

    Task<string> GetUserNameAsync(string accessToken);

    Task<string> GetServiceTokenAsync(string scope);
}
