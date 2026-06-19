extern alias ShortLinkApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShortLinkApi::ShortLink.Api.Filters;
using SliceFx;

namespace ShortLink.Api.Tests;

/// <summary>
/// Verifies that the two rate-limit partitions ("redirect" and "public-create") maintained by
/// <see cref="RateLimitStore"/> are independent — exhausting one does not throttle the other.
///
/// Before the <see cref="RateLimitStore"/> refactor, isolation was implied by each filter class
/// owning its own static <c>ConcurrentDictionary</c>. After the refactor both filters share a
/// single store instance; this test makes that invariant explicit and prevents regressions.
/// </summary>
public sealed class PartitionSeparationTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly SliceFilterDelegate PassThroughDelegate =
        _ => new ValueTask<SliceFilterResult>(SliceFilterResult.PassThrough(null, 200));

    private static SliceFilterContext MakeRedirectContext(string? clientIp) =>
        new(
            method: "GET",
            path: "/r/ABC123",
            headers: EmptyHeaders,
            routeValues: EmptyRouteValues,
            services: new ServiceCollection().BuildServiceProvider(),
            clientIp: clientIp,
            cancellationToken: CancellationToken.None);

    private static SliceFilterContext MakePublicCreateContext(string? clientIp) =>
        new(
            method: "POST",
            path: "/api/links/public",
            headers: EmptyHeaders,
            routeValues: EmptyRouteValues,
            services: new ServiceCollection().BuildServiceProvider(),
            clientIp: clientIp,
            cancellationToken: CancellationToken.None);

    [Fact]
    public async Task Exhausting_redirect_partition_does_not_throttle_public_create()
    {
        var time = new FakeTimeProvider();
        // Both filters share the same store instance, as they do in production.
        var store = new RateLimitStore();
        var redirectFilter = new RateLimitFilter(store, time, NullLogger<RateLimitFilter>.Instance);
        var publicCreateFilter = new PublicCreateRateLimitFilter(store, time, NullLogger<PublicCreateRateLimitFilter>.Instance);

        const string ip = "1.2.3.4";

        // Exhaust the redirect partition (limit 20).
        for (var i = 0; i <= 20; i++)
        {
            await redirectFilter.InvokeAsync(MakeRedirectContext(ip), PassThroughDelegate);
        }

        // The redirect partition must now be throttled.
        var redirectResult = await redirectFilter.InvokeAsync(MakeRedirectContext(ip), PassThroughDelegate);
        Assert.True(redirectResult.IsShortCircuit, "Redirect partition should be throttled after 21 requests.");

        // The public-create partition must still be open.
        var publicCreateCtx = MakePublicCreateContext(ip);
        var publicCreateResult = await publicCreateFilter.InvokeAsync(publicCreateCtx, PassThroughDelegate);
        Assert.False(publicCreateResult.IsShortCircuit, "Public-create partition must be independent of the redirect partition.");
        Assert.Equal("9", publicCreateCtx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Exhausting_public_create_partition_does_not_throttle_redirect()
    {
        var time = new FakeTimeProvider();
        var store = new RateLimitStore();
        var redirectFilter = new RateLimitFilter(store, time, NullLogger<RateLimitFilter>.Instance);
        var publicCreateFilter = new PublicCreateRateLimitFilter(store, time, NullLogger<PublicCreateRateLimitFilter>.Instance);

        const string ip = "5.6.7.8";

        // Exhaust the public-create partition (limit 10).
        for (var i = 0; i <= 10; i++)
        {
            await publicCreateFilter.InvokeAsync(MakePublicCreateContext(ip), PassThroughDelegate);
        }

        // The public-create partition must now be throttled.
        var publicResult = await publicCreateFilter.InvokeAsync(MakePublicCreateContext(ip), PassThroughDelegate);
        Assert.True(publicResult.IsShortCircuit, "Public-create partition should be throttled after 11 requests.");

        // The redirect partition must still be open.
        var redirectCtx = MakeRedirectContext(ip);
        var redirectResult = await redirectFilter.InvokeAsync(redirectCtx, PassThroughDelegate);
        Assert.False(redirectResult.IsShortCircuit, "Redirect partition must be independent of the public-create partition.");
        Assert.Equal("19", redirectCtx.ResponseHeaders["X-RateLimit-Remaining"]);
    }
}
