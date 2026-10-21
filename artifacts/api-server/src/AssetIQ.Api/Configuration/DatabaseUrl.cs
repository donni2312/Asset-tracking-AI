using Npgsql;

namespace AssetIQ.Api.Configuration;

/// <summary>
/// DATABASE_URL is a libpq URI (<c>postgres://user:pass@host:5433/assetiq?sslmode=require</c>, as
/// printed by <c>pnpm db:local</c> and provided by Replit); Npgsql wants key=value pairs.
/// A value that is already a key=value connection string is passed through unchanged.
/// </summary>
public static class DatabaseUrl
{
    public static string ToConnectionString(string databaseUrl)
    {
        var value = databaseUrl.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host.Trim('[', ']'),
            Port = uri.Port > 0 ? uri.Port : 5432,
        };
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (database.Length > 0) builder.Database = database;

        if (uri.UserInfo.Length > 0)
        {
            var credentials = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(credentials[0]);
            if (credentials.Length > 1) builder.Password = Uri.UnescapeDataString(credentials[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]).ToLowerInvariant();
            var setting = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = setting.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => throw new InvalidOperationException($"Unsupported sslmode in DATABASE_URL: {setting}"),
                    };
                    break;
                case "application_name":
                    builder.ApplicationName = setting;
                    break;
                case "connect_timeout":
                    if (int.TryParse(setting, out var seconds)) builder.Timeout = seconds;
                    break;
                case "options":
                    builder.Options = setting;
                    break;
            }
        }
        return builder.ConnectionString;
    }
}
