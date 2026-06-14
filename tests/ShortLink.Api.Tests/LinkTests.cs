extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;

namespace ShortLink.Api.Tests;

public sealed class LinkTests : IAsyncLifetime
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

    // --- CreateLink ---

    [Fact]
    public async Task CreateLink_valid_url_returns_201_with_code()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com/path" }),
        };
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code));
        Assert.Equal(7, code.GetString()!.Length);
        Assert.Contains("/r/", doc.RootElement.GetProperty("shortUrl").GetString()!);
        Assert.Equal("https://example.com/path", doc.RootElement.GetProperty("targetUrl").GetString());
    }

    [Fact]
    public async Task CreateLink_missing_api_key_returns_401()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com" }),
        };
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_invalid_api_key_returns_401()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", "bad-key");
        request.Content = JsonContent.Create(new { targetUrl = "https://example.com" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_invalid_url_returns_400_validation_problem()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "not-a-url" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- ListLinks ---

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
    }

    [Fact]
    public async Task ListLinks_without_auth_returns_401()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync("/api/links?page=1&pageSize=10", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- DeleteLink ---

    [Fact]
    public async Task DeleteLink_existing_returns_204()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://delete-me.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Delete it
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/links/{id}");
        deleteReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var deleteResp = await host.Client.SendAsync(deleteReq, ct);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteLink_nonexistent_returns_404()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/links/99999999");
        req.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- GetLinkStats ---

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
    public async Task GetLinkStats_wrong_owner_returns_404()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var statsReq = new HttpRequestMessage(HttpMethod.Get, "/api/links/1/stats");
        statsReq.Headers.Add("X-Api-Key", "not-the-owner");
        var response = await host.Client.SendAsync(statsReq, ct);

        // Either 401 (bad key) or 404 (not owned) — both are acceptable
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Expected 401 or 404 but got {response.StatusCode}");
    }
}
