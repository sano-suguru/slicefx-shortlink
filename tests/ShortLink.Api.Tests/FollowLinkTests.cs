extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShortLink.Api.Tests.Helpers;
using SliceFx.Testing;

namespace ShortLink.Api.Tests;

public sealed class FollowLinkTests : IAsyncLifetime
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
    public async Task FollowLink_existing_code_returns_302_with_location()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://redirect-target.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var code = createDoc.RootElement.GetProperty("code").GetString()!;

        // Use TestServer directly to get a no-redirect client
        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        var noRedirectClient = testServer.CreateClient();

        var response = await noRedirectClient.GetAsync($"/r/{code}", ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Normalize trailing slash — ASP.NET may append one
        var location = response.Headers.Location?.ToString().TrimEnd('/');
        Assert.Equal("https://redirect-target.example.com", location);
    }

    [Fact]
    public async Task FollowLink_unknown_code_returns_404()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        var noRedirectClient = testServer.CreateClient();

        var response = await noRedirectClient.GetAsync("/r/XXXXXXX", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FollowLink_records_click_in_stats()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://click-tracked.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var code = createDoc.RootElement.GetProperty("code").GetString()!;
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Follow the link (no-redirect client so we don't hit the external URL)
        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        await testServer.CreateClient().GetAsync($"/r/{code}", ct);

        // Check stats reflect the click
        var statsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{id}/stats");
        statsReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var statsResp = await host.Client.SendAsync(statsReq, ct);
        var statsBody = await statsResp.Content.ReadAsStringAsync(ct);
        using var statsDoc = JsonDocument.Parse(statsBody);

        Assert.Equal(1, statsDoc.RootElement.GetProperty("totalClicks").GetInt64());
    }

    [Fact]
    public async Task FollowLink_returns_429_after_rate_limit_exceeded()
    {
        // Rate limit is 20 per minute. The 21st request should return 429.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link to follow
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://rate-limit-test.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var code = createDoc.RootElement.GetProperty("code").GetString()!;

        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        var noRedirectClient = testServer.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 21; i++)
        {
            lastResponse = await noRedirectClient.GetAsync($"/r/{code}", ct);
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
        Assert.True(lastResponse.Headers.Contains("Retry-After"), "Expected Retry-After header on 429.");
        Assert.True(
            lastResponse.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0",
            "Expected X-RateLimit-Remaining: 0 on 429.");
    }

    [Fact]
    public async Task FollowLink_swallows_click_store_failure()
    {
        // Replace IClickStore with a stub that always throws.
        // The redirect should still succeed — click-store failures must not return 500.
        await using var host = TestHostFactory.Create(svc =>
            svc.Replace<ShortLinkApi::ShortLink.Api.Infrastructure.IClickStore>(new ThrowingClickStore()));
        var ct = TestContext.Current.CancellationToken;

        // Create a link
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://swallow-failure.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var code = createDoc.RootElement.GetProperty("code").GetString()!;

        // Follow the link via no-redirect client — should be 302, not 500
        var testServer = (TestServer)host.Services.GetRequiredService<IServer>();
        var noRedirectClient = testServer.CreateClient();
        var response = await noRedirectClient.GetAsync($"/r/{code}", ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
