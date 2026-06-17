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

    // Bootstrap is called once per test process. Direct execution guarantees this
    // because DisableTestParallelization = true in AssemblyInfo.cs.
    private static bool _bootstrapped;

    public static async Task ClearLinksAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        // Reset in-process rate-limit state so tests are isolated from each other.
        // Tests run serially (DisableTestParallelization in AssemblyInfo.cs), so this is safe.
        RateLimitFilter.ResetForTests();

        // Bootstrap schema exactly once per process. schema.sql uses CREATE TABLE IF NOT EXISTS
        // and seed inserts use ON CONFLICT DO NOTHING, so it is idempotent on subsequent calls,
        // but calling it hundreds of times is wasteful. The static bool guard avoids that.
        if (!_bootstrapped)
        {
            await Db.BootstrapAsync(ds, SeedApiKey, ct);
            await SeedSecondApiKeyAsync(ds, ct);
            _bootstrapped = true;
        }

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
