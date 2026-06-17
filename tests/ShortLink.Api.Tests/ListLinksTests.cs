extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;

namespace ShortLink.Api.Tests;

public sealed class ListLinksTests : IAsyncLifetime
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
    public async Task ListLinks_without_query_string_returns_200_with_defaults()
    {
        // page/pageSize are now optional (int?) — omitting them must not return 400.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/links");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        // Defaults: page=1, pageSize=20
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ListLinks_returns_paged_results()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create two links
        for (var i = 0; i < 2; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/links");
            req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
            req.Content = JsonContent.Create(new { targetUrl = $"https://example.com/{i}" });
            await host.Client.SendAsync(req, ct);
        }

        var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/links?page=1&pageSize=10");
        listReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var listResp = await host.Client.SendAsync(listReq, ct);

        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var body = await listResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("items").GetArrayLength() >= 2);
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.True(doc.RootElement.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task ListLinks_without_auth_returns_401()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync("/api/links?page=1&pageSize=10", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListLinks_returns_only_own_links()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create one link for each owner
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        req1.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        req1.Content = JsonContent.Create(new { targetUrl = "https://owner1.example.com" });
        await host.Client.SendAsync(req1, ct);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        req2.Headers.Add("X-Api-Key", TestDb.SecondApiKey);
        req2.Content = JsonContent.Create(new { targetUrl = "https://owner2.example.com" });
        await host.Client.SendAsync(req2, ct);

        // Each owner should see only their own link
        var listReq1 = new HttpRequestMessage(HttpMethod.Get, "/api/links");
        listReq1.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var listResp1 = await host.Client.SendAsync(listReq1, ct);
        var body1 = await listResp1.Content.ReadAsStringAsync(ct);
        using var doc1 = JsonDocument.Parse(body1);
        var items1 = doc1.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items1, item =>
            Assert.Contains("owner1.example.com", item.GetProperty("targetUrl").GetString()!));

        var listReq2 = new HttpRequestMessage(HttpMethod.Get, "/api/links");
        listReq2.Headers.Add("X-Api-Key", TestDb.SecondApiKey);
        var listResp2 = await host.Client.SendAsync(listReq2, ct);
        var body2 = await listResp2.Content.ReadAsStringAsync(ct);
        using var doc2 = JsonDocument.Parse(body2);
        var items2 = doc2.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items2, item =>
            Assert.Contains("owner2.example.com", item.GetProperty("targetUrl").GetString()!));
    }

    [Fact]
    public async Task ListLinks_page_0_is_clamped_to_1()
    {
        // page=0 is below the valid range [1, 10000]; Handle clamps it to 1.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/links?page=0");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task ListLinks_page_over_10000_is_clamped_to_10000()
    {
        // page=99999 exceeds the max; Handle clamps it to 10000.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/links?page=99999");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(10000, doc.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task ListLinks_pageSize_over_100_is_clamped_to_100()
    {
        // pageSize=999 exceeds the max; Handle clamps it to 100.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/links?pageSize=999");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ListLinks_negative_pageSize_is_clamped_to_1()
    {
        // pageSize=-5 is below the valid range [1, 100]; Handle clamps it to 1.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/links?pageSize=-5");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("pageSize").GetInt32());
    }
}
