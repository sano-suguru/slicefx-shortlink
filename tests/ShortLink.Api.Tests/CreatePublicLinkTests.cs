extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class CreatePublicLinkTests : IAsyncLifetime
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
    public async Task CreatePublicLink_Returns201_WithShortUrl()
    {
        // No X-Api-Key header — the public endpoint must accept anonymous requests.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links/public")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com/public" }),
        };
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code));
        Assert.Equal(7, code.GetString()!.Length);
        Assert.Contains("/r/", doc.RootElement.GetProperty("shortUrl").GetString()!);
        Assert.Equal("https://example.com/public", doc.RootElement.GetProperty("targetUrl").GetString());
    }

    [Fact]
    public async Task CreatePublicLink_StoresAnonymousOwner()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links/public")
        {
            Content = JsonContent.Create(new { targetUrl = "https://example.com/anon-owner" }),
        };
        var response = await host.Client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.GetProperty("id").GetInt64();

        await using var conn = await _ds!.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT owner_key_hash FROM links WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        var ownerKeyHash = (string?)await cmd.ExecuteScalarAsync(ct);

        Assert.Equal(AnonymousOwner.KeyHash, ownerKeyHash);
    }

    [Fact]
    public async Task CreatePublicLink_InvalidScheme_Returns400()
    {
        // ftp:// passes the [Url] DataAnnotation (absolute URI) but is blocked by CreateLinkRequestValidator,
        // which keys on CreateLinkRequest and therefore runs on the public endpoint too.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links/public")
        {
            Content = JsonContent.Create(new { targetUrl = "ftp://example.com/file.zip" }),
        };
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePublicLink_PrivateHost_Returns400()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links/public")
        {
            Content = JsonContent.Create(new { targetUrl = "http://192.168.1.1/secret" }),
        };
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePublicLink_RateLimit_Returns429()
    {
        // Each TestHostFactory.Create() produces its own DI container and therefore its own
        // RateLimitStore singleton — no manual reset required between tests.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        // The limit is 10 per minute per IP. The first 10 rapid requests succeed; the 11th is throttled.
        // All requests share the same in-process bucket (single TestHost, single client IP).
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/links/public")
            {
                Content = JsonContent.Create(new { targetUrl = $"https://example.com/rl/{i}" }),
            };
            last = await host.Client.SendAsync(request, ct);
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.NotEmpty(retryAfter);
    }
}
