using System;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace HonzaBotner;

public static class PsqlConnectionStringParser
{
    internal static string GetEFConnectionString(string? connectionUrl)
    {
        if (string.IsNullOrWhiteSpace(connectionUrl))
            throw new InvalidOperationException("DATABASE_URL is required.");

        if (!Uri.TryCreate(connectionUrl, UriKind.Absolute, out Uri? url) ||
            (url.Scheme != "postgres" && url.Scheme != "postgresql"))
            return connectionUrl;

        string[] credentials = url.UserInfo.Split(':', 2);
        if (credentials.Length != 2)
            throw new InvalidOperationException("DATABASE_URL must contain a username and password.");

        bool local = url.IsLoopback || url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        NpgsqlConnectionStringBuilder builder = new()
        {
            Host = url.Host,
            Port = url.IsDefaultPort ? 5432 : url.Port,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            Database = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')),
            Pooling = true,
            SslMode = local ? SslMode.Disable : SslMode.VerifyFull
        };

        foreach ((string key, Microsoft.Extensions.Primitives.StringValues values) in
                 QueryHelpers.ParseQuery(url.Query))
        {
            string value = values.ToString();
            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    if (!Enum.TryParse(value.Replace("-", ""), true, out SslMode sslMode))
                        throw new InvalidOperationException("DATABASE_URL contains an invalid sslmode.");
                    if (!local && sslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
                        throw new InvalidOperationException("Remote DATABASE_URL must require TLS.");
                    builder.SslMode = sslMode;
                    break;
                case "sslrootcert":
                    builder.RootCertificate = value;
                    break;
                case "application_name":
                    builder.ApplicationName = value;
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
