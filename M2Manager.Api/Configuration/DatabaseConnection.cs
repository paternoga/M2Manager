using Npgsql;

namespace M2Manager.Api.Configuration;

/// <summary>
/// Neon i Render podają połączenie jako URI (postgresql://user:pass@host/db?sslmode=require),
/// a Npgsql oczekuje formatu klucz=wartość. Ta klasa przyjmuje jedno i drugie.
/// </summary>
public static class DatabaseConnection
{
    public static string? Resolve(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration["DATABASE_URL"];
        }

        return string.IsNullOrWhiteSpace(raw) ? null : Normalize(raw);
    }

    public static string Normalize(string raw)
    {
        var value = raw.Trim();

        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,

            // Neon wymaga TLS; certyfikat jest publiczny, więc pełna weryfikacja bywa zbędna.
            SslMode = SslMode.Require
        };

        foreach (var pair in ParseQuery(uri.Query))
        {
            switch (pair.Key.ToLowerInvariant())
            {
                case "sslmode":
                    if (Enum.TryParse<SslMode>(pair.Value, ignoreCase: true, out var sslMode))
                    {
                        builder.SslMode = sslMode;
                    }

                    break;

                case "application_name":
                    builder.ApplicationName = pair.Value;
                    break;

                // channel_binding, options i pozostałe parametry Neona świadomie pomijamy.
            }
        }

        return builder.ConnectionString;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                yield return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(kv[0]),
                    Uri.UnescapeDataString(kv[1]));
            }
        }
    }
}
