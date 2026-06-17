extern alias ShortLinkApi;

using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class PostgresClickStoreTests : DbBackedTest
{
    private PostgresLinkStore LinkStore => new(Ds);
    private PostgresClickStore ClickStore => new(Ds);

    private static string Owner1Hash => ApiKeyValidator.Hash(TestDb.SeedApiKey);

    [Fact]
    public async Task RecordAsync_increments_total_clicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var link = await LinkStore.CreateAsync("https://example.com/click-test", Owner1Hash, ShortCode.Generate(), ct);
        var clickStore = ClickStore;

        await clickStore.RecordAsync(link.Id, referer: null, userAgent: null, ct);
        var stats = await clickStore.GetStatsAsync(link.Id, ct);

        Assert.Equal(1, stats.TotalClicks);
    }

    [Fact]
    public async Task GetStatsAsync_returns_empty_stats_for_no_clicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var link = await LinkStore.CreateAsync("https://example.com/no-clicks", Owner1Hash, ShortCode.Generate(), ct);

        var stats = await ClickStore.GetStatsAsync(link.Id, ct);

        Assert.Equal(0, stats.TotalClicks);
        Assert.Empty(stats.Daily);
    }

    [Fact]
    public async Task GetStatsAsync_daily_aggregation()
    {
        var ct = TestContext.Current.CancellationToken;
        var link = await LinkStore.CreateAsync("https://example.com/daily-agg", Owner1Hash, ShortCode.Generate(), ct);

        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        // Insert 2 clicks for today and 1 click for yesterday directly.
        await using var conn = await Ds.OpenConnectionAsync(ct);

        for (var i = 0; i < 2; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO clicks (link_id, referer, user_agent, clicked_at) VALUES ($1, $2, $3, $4)";
            cmd.Parameters.AddWithValue(link.Id);
            cmd.Parameters.AddWithValue(DBNull.Value);
            cmd.Parameters.AddWithValue(DBNull.Value);
            cmd.Parameters.AddWithValue(today);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmdYesterday = conn.CreateCommand();
        cmdYesterday.CommandText = "INSERT INTO clicks (link_id, referer, user_agent, clicked_at) VALUES ($1, $2, $3, $4)";
        cmdYesterday.Parameters.AddWithValue(link.Id);
        cmdYesterday.Parameters.AddWithValue(DBNull.Value);
        cmdYesterday.Parameters.AddWithValue(DBNull.Value);
        cmdYesterday.Parameters.AddWithValue(yesterday);
        await cmdYesterday.ExecuteNonQueryAsync(ct);

        var stats = await ClickStore.GetStatsAsync(link.Id, ct);

        Assert.Equal(3, stats.TotalClicks);
        Assert.Equal(2, stats.Daily.Count);

        // Daily is sorted descending — today first.
        Assert.Equal(DateOnly.FromDateTime(today), stats.Daily[0].Date);
        Assert.Equal(2, stats.Daily[0].Count);
        Assert.Equal(DateOnly.FromDateTime(yesterday), stats.Daily[1].Date);
        Assert.Equal(1, stats.Daily[1].Count);
    }

    [Fact]
    public async Task GetStatsAsync_limits_daily_to_30_days()
    {
        var ct = TestContext.Current.CancellationToken;
        var link = await LinkStore.CreateAsync("https://example.com/30-day-limit", Owner1Hash, ShortCode.Generate(), ct);

        var baseDate = DateTime.UtcNow.Date;

        await using var conn = await Ds.OpenConnectionAsync(ct);

        for (var i = 0; i < 35; i++)
        {
            var day = baseDate.AddDays(-i);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO clicks (link_id, referer, user_agent, clicked_at) VALUES ($1, $2, $3, $4)";
            cmd.Parameters.AddWithValue(link.Id);
            cmd.Parameters.AddWithValue(DBNull.Value);
            cmd.Parameters.AddWithValue(DBNull.Value);
            cmd.Parameters.AddWithValue(day);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var stats = await ClickStore.GetStatsAsync(link.Id, ct);

        Assert.Equal(35, stats.TotalClicks);
        Assert.True(stats.Daily.Count <= 30);
    }
}
