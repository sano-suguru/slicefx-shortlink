extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests.Helpers;

internal static class TestDb
{
    private const string DefaultConnStr =
        "Host=localhost;Username=postgres;Password=postgres;Database=shortlink;SSL Mode=Prefer";

    internal const string SeedApiKey = "dev-seed-key-change-in-production";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? DefaultConnStr;

    public static NpgsqlDataSource Build() => Db.Build(ConnectionString);

    public static async Task ClearLinksAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        // Bootstrap schema before truncating so this works on a fresh database (e.g. CI service container).
        // schema.sql uses CREATE TABLE IF NOT EXISTS and the seed insert uses ON CONFLICT DO NOTHING,
        // so this call is idempotent when tables already exist.
        await Db.BootstrapAsync(ds, SeedApiKey, ct);

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE links, clicks RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
