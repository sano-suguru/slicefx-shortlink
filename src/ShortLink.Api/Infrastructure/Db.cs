using Npgsql;

namespace ShortLink.Api.Infrastructure;

public static class Db
{
    public static NpgsqlDataSource Build(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        return builder.Build();
    }

    public static async Task BootstrapAsync(NpgsqlDataSource dataSource, string seedApiKey, CancellationToken ct = default)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Db", "schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath, ct);

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        foreach (var statement in schemaSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var keyHash = ApiKeyValidator.Hash(seedApiKey);
        await using var seedCmd = conn.CreateCommand();
        seedCmd.CommandText = """
            INSERT INTO api_keys (key_hash, label)
            VALUES ($1, 'seed')
            ON CONFLICT (key_hash) DO NOTHING
            """;
        seedCmd.Parameters.AddWithValue(keyHash);
        await seedCmd.ExecuteNonQueryAsync(ct);
    }

    public static string ResolveConnectionString(IConfiguration config)
    {
        var uri = config["DATABASE_URL"];
        if (!string.IsNullOrEmpty(uri))
        {
            return NormalizeUri(uri);
        }

        return config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("No Postgres connection string configured. Set ConnectionStrings:Postgres or DATABASE_URL.");
    }

    private static string NormalizeUri(string uri)
    {
        var u = new Uri(uri);
        // System.Uri returns percent-encoded userInfo — decode before passing to Npgsql.
        // Neon postgres:// URLs frequently contain %-escaped characters in passwords.
        var userInfo = u.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = u.Host,
            Port = u.Port > 0 ? u.Port : 5432,
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : null,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.Require,
            Timeout = 30,
        };
        return b.ConnectionString;
    }
}
