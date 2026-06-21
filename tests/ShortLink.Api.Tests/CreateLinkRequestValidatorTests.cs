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

    // --- Newly blocked ranges (0.0.0.0 and 169.254/16) ---

    [Fact]
    public async Task All_zero_0_0_0_0_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://0.0.0.0/", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Link_local_169_254_169_254_fails()
    {
        // Cloud metadata endpoint on AWS/GCP/Azure — must be blocked as phishing redirect bait.
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://169.254.169.254/latest/meta-data/", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Link_local_169_254_0_1_fails()
    {
        // Any address in 169.254.0.0/16 is link-local.
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://169.254.0.1/", ct);
        Assert.False(result.IsValid);
    }

    // --- IPv6 loopback fixed ---

    [Fact]
    public async Task IPv6_loopback_fails()
    {
        // ::1 is IPv6 loopback; already blocked via IPAddress.IsLoopback. Pinned as regression guard.
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://[::1]/admin", ct);
        Assert.False(result.IsValid);
    }

    // --- Decimal / octal / hex IP literals: already covered by System.Uri normalization ---
    // System.Uri.Host normalises these forms to dotted-quad BEFORE we call IPAddress.TryParse.
    // e.g. "http://2130706433/" -> Host = "127.0.0.1" (IsLoopback = true).
    // These tests document that the existing code handles them without any extra uint32 logic.

    [Fact]
    public async Task Decimal_ip_127_0_0_1_fails()
    {
        // 2130706433 = 127.0.0.1 in decimal. System.Uri normalises it to "127.0.0.1".
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://2130706433/", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Octal_ip_0177_0_0_1_fails()
    {
        // 0177.0.0.1 is octal-dotted for 127.0.0.1. System.Uri normalises it.
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://0177.0.0.1/secret", ct);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Hex_ip_0x7f_0_0_1_fails()
    {
        // 0x7f.0.0.1 is hex-dotted for 127.0.0.1. System.Uri normalises it.
        var ct = TestContext.Current.CancellationToken;
        var result = await ValidateAsync("http://0x7f.0.0.1/secret", ct);
        Assert.False(result.IsValid);
    }
}
