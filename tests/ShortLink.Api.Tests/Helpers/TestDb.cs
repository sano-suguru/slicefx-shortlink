extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Filters;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests.Helpers;

internal static class TestDb
{
    private const string DefaultConnStr =
        "Host=localhost;Username=postgres;Password=postgres;Database=shortlink;SSL Mode=Prefer";

    internal const string SeedApiKey = "dev-seed-key-change-in-production";

    // A second distinct API key used to verify per-owner isolation (tenant separation).
    internal const string SecondApiKey = "test-second-owner-key-isolation";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? DefaultConnStr;

    public static NpgsqlDataSource Build() => Db.Build(ConnectionString);

    public static async Task ClearLinksAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        // Reset in-process rate-limit state so tests are isolated from each other.
        // Tests run serially (DisableTestParallelization in AssemblyInfo.cs), so this is safe.
        RateLimitFilter.ResetForTests();

        // Bootstrap schema before truncating so this works on a fresh database (e.g. CI service container).
        // schema.sql uses CREATE TABLE IF NOT EXISTS and the seed insert uses ON CONFLICT DO NOTHING,
        // so this call is idempotent when tables already exist.
        await Db.BootstrapAsync(ds, SeedApiKey, ct);

        // Ensure the second test key exists (idempotent via ON CONFLICT DO NOTHING).
        await SeedSecondApiKeyAsync(ds, ct);

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE links, clicks RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Inserts the second owner API key used for tenant-isolation tests.</summary>
    public static async Task SeedSecondApiKeyAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        var hash = ApiKeyValidator.Hash(SecondApiKey);
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (key_hash, label)
            VALUES ($1, 'second-test-key')
            ON CONFLICT (key_hash) DO NOTHING
            """;
        cmd.Parameters.AddWithValue(hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
