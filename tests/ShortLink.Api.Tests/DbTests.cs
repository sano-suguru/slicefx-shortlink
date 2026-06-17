extern alias ShortLinkApi;

using Microsoft.Extensions.Configuration;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

/// <summary>
/// Unit tests for <see cref="Db.ResolveConnectionString"/> and the private NormalizeUri path.
/// Uses real <see cref="ConfigurationBuilder"/> with in-memory collections — no DB required.
/// </summary>
public sealed class DbTests
{
    private static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    // --- ResolveConnectionString priority ---

    [Fact]
    public void ResolveConnectionString_prefers_DATABASE_URL_over_connection_string()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://user:pass@db.example.com:5432/mydb",
            ["ConnectionStrings:Postgres"] = "Host=other;Database=other",
        });

        var result = Db.ResolveConnectionString(config);

        // Must contain the host from DATABASE_URL, not from ConnectionStrings:Postgres.
        Assert.Contains("db.example.com", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("other", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveConnectionString_falls_back_to_ConnectionStrings_Postgres_when_no_DATABASE_URL()
    {
        const string connStr = "Host=localhost;Username=pg;Database=shortlink";
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connStr,
        });

        var result = Db.ResolveConnectionString(config);

        Assert.Equal(connStr, result);
    }

    [Fact]
    public void ResolveConnectionString_throws_when_neither_source_is_set()
    {
        var config = BuildConfig([]);

        Assert.Throws<InvalidOperationException>(() => Db.ResolveConnectionString(config));
    }

    // --- NormalizeUri (exercised via DATABASE_URL path) ---

    [Fact]
    public void NormalizeUri_decodes_percent_encoded_password()
    {
        // Neon-style URL with a %-escaped character in the password.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://alice:p%40ssword@neon.example.com:5432/mydb",
        });

        var result = Db.ResolveConnectionString(config);

        // The decoded password should appear in the connection string, not the raw percent form.
        Assert.Contains("p@ssword", result, StringComparison.Ordinal);
        Assert.DoesNotContain("p%40ssword", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeUri_sets_ssl_mode_require()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://user:pass@db.example.com:5432/mydb",
        });

        var result = Db.ResolveConnectionString(config);

        Assert.Contains("Require", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeUri_extracts_host_port_database()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://dbuser:secret@pg.example.com:5433/appdb",
        });

        var result = Db.ResolveConnectionString(config);

        Assert.Contains("pg.example.com", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5433", result);
        Assert.Contains("appdb", result, StringComparison.OrdinalIgnoreCase);
    }
}
