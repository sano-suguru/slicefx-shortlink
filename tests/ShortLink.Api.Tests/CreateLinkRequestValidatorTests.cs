extern alias ShortLinkApi;
using ShortLink.Contracts;
using ShortLinkApi::ShortLink.Api.Features.Links;
using SliceFx;

namespace ShortLink.Api.Tests;

public sealed class CreateLinkRequestValidatorTests
{
    private static ValueTask<SliceValidationResult> ValidateAsync(string url, CancellationToken ct = default)
    {
        var validator = new CreateLinkRequestValidator();
        return validator.ValidateAsync(new CreateLinkRequest { TargetUrl = url }, ct);
    }

    // --- Valid URLs ---

    [Fact]
    public async Task Valid_http_url_passes()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://example.com/path", ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Valid_https_url_passes()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("https://example.com/path", ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Public_url_passes()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("https://example.com", ct);
        Assert.True(result.IsValid);
    }

    // --- Invalid format ---

    [Fact]
    public async Task Invalid_format_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("not-a-url", ct);
        Assert.False(result.IsValid);
    }

    // --- Disallowed schemes ---

    [Fact]
    public async Task Ftp_scheme_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("ftp://example.com/file.txt", ct);
        Assert.False(result.IsValid);
    }

    // --- Loopback ---

    [Fact]
    public async Task Named_loopback_localhost_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://localhost/admin", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Loopback_127_0_0_1_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://127.0.0.1/secret", ct);
        Assert.False(result.IsValid);
    }

    // --- Private IPv4 ranges ---

    [Fact]
    public async Task Private_192_168_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://192.168.1.1/secret", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Private_10_x_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://10.0.0.1/secret", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Private_172_16_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://172.16.0.1/secret", ct);
        Assert.False(result.IsValid);
    }

    // --- IPv6 private/reserved ranges ---

    [Fact]
    public async Task ULA_ipv6_fd00_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://[fd00::1]/admin", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task IPv4_mapped_ipv6_loopback_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://[::ffff:10.0.0.1]/secret", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task IPv4_mapped_ipv6_private_192_168_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://[::ffff:192.168.1.1]/secret", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Link_local_ipv6_fe80_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://[fe80::1]/secret", ct);
        Assert.False(result.IsValid);
    }
}
