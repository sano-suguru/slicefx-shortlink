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

    // --- ISliceValidator<T> (#4: AOT validator path) ---

    [Fact]
    public async Task CreateLink_ftp_scheme_returns_400_from_slice_validator()
    {
        // ISliceValidator fires after [Required,Url] DataAnnotations pass for absolute URIs.
        // ftp:// passes Url attribute (it is an absolute URI) but is blocked by CreateLinkRequestValidator.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "ftp://example.com/file.zip" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_localhost_url_returns_400_from_slice_validator()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "http://localhost/admin" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_private_ip_url_returns_400_from_slice_validator()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "http://192.168.1.1/secret" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ipv6_ula_returns_400_from_slice_validator()
    {
        // fd00::/8 is ULA (Unique Local Address) — private IPv6 range.
        // Previously slipped through because bytes.Length != 4 short-circuited to false.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "http://[fd00::1]/admin" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ipv4_mapped_ipv6_returns_400_from_slice_validator()
    {
        // ::ffff:10.0.0.1 is IPv4-mapped IPv6 for 10.0.0.1 — private address.
        // Previously slipped through because bytes.Length != 4 short-circuited to false.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "http://[::ffff:10.0.0.1]/secret" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- [FromHeader] binding (#8: SliceAotArgumentBinder header-bind path) ---

    [Fact]
    public async Task CreateLink_with_request_id_header_echoes_it_in_response()
    {
        // Exercises [FromHeader(Name="X-Request-Id")] in Handle signature.
        // The value is echoed as requestId in the response body.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com" }),
        };
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Headers.Add("X-Request-Id", "trace-abc-123");
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("trace-abc-123", doc.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task CreateLink_without_request_id_header_returns_null_requestId()
    {
        // [FromHeader] optional (string?) — absent header → null in response.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com" }),
        };
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        // requestId property should be absent or null when header is not sent
        if (doc.RootElement.TryGetProperty("requestId", out var requestIdProp))
        {
            Assert.Equal(JsonValueKind.Null, requestIdProp.ValueKind);
        }
    }

    // --- ListLinks ---

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

    // --- Tenant isolation (owner separation) ---
    // These tests use SecondApiKey (a distinct valid key) to exercise the
    // link.OwnerKeyHash != key.OwnerId 404 branch that was previously unreachable.

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
}
