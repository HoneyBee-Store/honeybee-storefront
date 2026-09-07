using Npgsql;

namespace HoneyBee.Web.Data;

/// <summary>
/// Accepts a database URL as well as a normal connection string.
///
/// Railway, Render, Fly and Heroku all hand the application a URL —
/// postgresql://user:password@host:5432/database — because that is what the
/// Postgres client libraries in most other languages expect. Npgsql does not
/// read that form, and the failure is an unhelpful parse error at startup, so
/// the URL is translated here instead.
/// </summary>
public static class ConnectionStrings
{
    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var trimmed = value.Trim();

        var isUrl = trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                 || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!isUrl) return trimmed;

        var uri = new Uri(trimmed);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null
        };

        // Managed Postgres is reached over the public internet and refuses
        // unencrypted connections. Left to the default the first connection
        // fails, so encryption is assumed for anything that is not local.
        var isLocal = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || uri.Host is "127.0.0.1" or "::1";

        if (!isLocal) builder.SslMode = SslMode.Require;

        // An explicit ?sslmode= in the URL wins over that assumption.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            if (parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(parts[1], ignoreCase: true, out var mode))
            {
                builder.SslMode = mode;
            }
        }

        return builder.ConnectionString;
    }
}
