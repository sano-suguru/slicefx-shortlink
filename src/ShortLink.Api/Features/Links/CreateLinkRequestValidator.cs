using System.Net;
using ShortLink.Contracts;

namespace ShortLink.Api.Features.Links;

// Validates CreateLinkRequest beyond DataAnnotations ([Required, Url]).
//
// Purpose: best-effort open-redirect / phishing-URL guard.
// - scheme must be http or https
// - host must not be localhost, loopback, or private IP range (best-effort)
//
// NOTE: This is NOT an SSRF guard. The app only redirects (SliceResult.Redirect)
// — it never fetches the target URL server-side. DNS rebinding is not defended
// against by string-level host checks. This validator's primary purpose is to
// exercise the ISliceValidator<T> execution path under ASP.NET NativeAOT (#4).
public sealed class CreateLinkRequestValidator : ISliceValidator<CreateLinkRequest>
{
    private static readonly string[] AllowedSchemes = ["http", "https"];

    public ValueTask<SliceValidationResult> ValidateAsync(CreateLinkRequest value, CancellationToken ct)
    {
        if (!Uri.TryCreate(value.TargetUrl, UriKind.Absolute, out var uri))
        {
            return ValueTask.FromResult(
                SliceValidationResult.Failure("TargetUrl", "Invalid URL format."));
        }

        if (!AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(
                SliceValidationResult.Failure("TargetUrl", $"Only http and https URLs are allowed. Got scheme: {uri.Scheme}"));
        }

        if (IsPrivateOrLoopbackHost(uri.Host))
        {
            return ValueTask.FromResult(
                SliceValidationResult.Failure("TargetUrl", "URLs targeting private or loopback addresses are not allowed."));
        }

        return ValueTask.FromResult(SliceValidationResult.Success);
    }

    private static bool IsPrivateOrLoopbackHost(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        // Named loopback
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        // Unwrap IPv4-mapped IPv6 (::ffff:10.0.0.1 etc.) before range checks.
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();

        if (bytes.Length == 16)
        {
            // IPv6: block link-local (fe80::/10), site-local (fec0::/10, deprecated),
            // and ULA (fc00::/7).
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        }

        // IPv4 private ranges
        // 0.0.0.0 — "this host on this network"; unroutable and meaningless as a redirect target.
        if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
        {
            return true;
        }

        // 10.0.0.0/8
        if (bytes[0] == 10)
        {
            return true;
        }

        // 169.254.0.0/16 — link-local (APIPA / cloud metadata, e.g. 169.254.169.254 on AWS/GCP/Azure).
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        return false;
    }
}
