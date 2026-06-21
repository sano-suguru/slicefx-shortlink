extern alias ShortLinkApi;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Infrastructure;
using SliceFx.Testing;

namespace ShortLink.Api.Tests;

public sealed class CreateLinkTests : IAsyncLifetime
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

    [Fact]
    public async Task CreateLink_empty_body_returns_400_problem()
    {
        // POST with empty JSON object {} — targetUrl is required, so validation should fail.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_missing_targetUrl_returns_400_problem()
    {
        // POST body without targetUrl field entirely — [Required] should fire.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { unrelated = "value" });
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

    // --- #4: DB failure injection ---

    [Fact]
    public async Task CreateLink_db_failure_returns_500_not_201()
    {
        await using var host = TestHostFactory.Create(svc =>
            svc.Replace<ILinkStore>(new ThrowingLinkStore()));
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = "https://example.com" });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // --- #5: malformed / boundary inputs ---

    [Fact]
    public async Task CreateLink_wrong_content_type_returns_415()
    {
        // Minimal API rejects non-JSON content-type with 415 Unsupported Media Type.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = new StringContent("targetUrl=https://example.com", Encoding.UTF8, "text/plain");
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_truncated_json_returns_400()
    {
        // Incomplete JSON body — Minimal API returns 400 (body deserialization failure).
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = new StringContent("{", Encoding.UTF8, "application/json");
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_targetUrl_over_2048_chars_returns_400()
    {
        // [StringLength(2048)] on CreateLinkRequest.TargetUrl — DataAnnotations validation fires.
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var longUrl = "https://example.com/" + new string('a', 2040); // total 2060 chars > 2048

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links");
        request.Headers.Add("X-Api-Key", TestDb.SeedApiKey);
        request.Content = JsonContent.Create(new { targetUrl = longUrl });
        var response = await host.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
