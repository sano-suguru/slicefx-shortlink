extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;

namespace ShortLink.Api.Tests;

public sealed class DeleteLinkTests : IAsyncLifetime
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

    [Fact]
    public async Task DeleteLink_valid_key_but_different_owner_returns_404()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // Create a link owned by the seed key
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        createReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        createReq.Content = JsonContent.Create(new { targetUrl = "https://delete-isolation.example.com" });
        var createResp = await host.Client.SendAsync(createReq, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetInt64();

        // Attempt to delete with a different valid key → 404 (cross-owner delete rejected)
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/links/{id}");
        deleteReq.Headers.Add("X-Api-Key", TestDb.SecondApiKey);
        var deleteResp = await host.Client.SendAsync(deleteReq, ct);

        Assert.Equal(HttpStatusCode.NotFound, deleteResp.StatusCode);

        // Verify the link still exists for the original owner
        var statsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{id}/stats");
        statsReq.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var statsResp = await host.Client.SendAsync(statsReq, ct);
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);
    }
}
