extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShortLink.Api.Tests.Helpers;

namespace ShortLink.Api.Tests;

public sealed class GetLinkStatsTests : IAsyncLifetime
{
    private NpgsqlDataSource? _ds;

    public async ValueTask InitializeAsync()
    {
        _ds = TestDb.Build();
        await TestDb.ClearLinksAsync(_ds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ds is not null) { await _ds.DisposeAsync(); }
    }

    [Fact]
    public async Task GetLinkStats_returns_stats()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://stats-test.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Get stats
        var statsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{id}/stats");
        statsReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var statsResp = await host.Client.SendAsync(statsReq, ct);

        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);
        var body = await statsResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(0, doc.RootElement.GetProperty("totalClicks").GetInt64());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("daily").ValueKind);
    }

    [Fact]
    public async Task GetLinkStats_invalid_key_returns_401()
    {
        // "not-the-owner" is not a registered API key → ApiKeyAuthFilter rejects with 401.
        // Named "wrong_owner" previously but the key is outright invalid, not a cross-owner access.
        // The strict 401 oracle documents this distinction.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var statsReq = new HttpRequestMessage(HttpMethod.Get, "/api/links/1/stats");
        statsReq.Headers.Add("X-Api-Key", "not-the-owner");
        var response = await host.Client.SendAsync(statsReq, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLinkStats_valid_key_but_different_owner_returns_404()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link owned by the seed key
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://owner-isolation.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Attempt to read stats with a different valid key → 404 (not 401 and not 200)
        var statsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{id}/stats");
        statsReq.Headers.Add("X-Api-Key", TestDb.SecondApiKey);
        var statsResp = await host.Client.SendAsync(statsReq, ct);

        Assert.Equal(HttpStatusCode.NotFound, statsResp.StatusCode);
    }

    [Fact]
    public async Task GetLinkStats_returns_daily_array()
    {
        // Create a link, follow it via a no-redirect client (recording a click),
        // then verify GetStats returns a daily array with at least one entry.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://daily-stats.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var code = createDoc.RootElement.GetProperty("code").GetString()!;
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Follow the link via no-redirect client to record a click
        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        var noRedirectClient = testServer.CreateClient();
        await noRedirectClient.GetAsync($"/r/{code}", ct);

        // Retrieve stats and check the daily breakdown
        var statsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{id}/stats");
        statsReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var statsResp = await host.Client.SendAsync(statsReq, ct);

        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);
        var body = await statsResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("totalClicks").GetInt64());

        var daily = doc.RootElement.GetProperty("daily");
        Assert.Equal(JsonValueKind.Array, daily.ValueKind);
        Assert.True(daily.GetArrayLength() >= 1, "Expected at least one daily entry after a click.");

        // Each entry should have Date and Count fields
        foreach (var entry in daily.EnumerateArray())
        {
            Assert.True(entry.TryGetProperty("date", out _), "Daily entry missing 'date' field.");
            Assert.True(entry.TryGetProperty("count", out var countProp), "Daily entry missing 'count' field.");
            Assert.True(countProp.GetInt64() > 0, "Daily count should be positive.");
        }
    }
}
