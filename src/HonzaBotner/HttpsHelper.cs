using Microsoft.AspNetCore.Builder;

namespace HonzaBotner;

public static class ReverseProxyHttpsEnforcerExtensions
{
    public static IApplicationBuilder UseReverseProxyHttpsEnforcer(this IApplicationBuilder builder)
    {
        return builder.UseHttpsRedirection();
    }
}
