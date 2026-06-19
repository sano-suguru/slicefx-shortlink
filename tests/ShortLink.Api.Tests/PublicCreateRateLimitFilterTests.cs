extern alias ShortLinkApi;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShortLinkApi::ShortLink.Api.Filters;
using SliceFx;

namespace ShortLink.Api.Tests;

public sealed class PublicCreateRateLimitFilterTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly SliceFilterDelegate PassThroughDelegate =
        _ => new ValueTask<SliceFilterResult>(SliceFilterResult.PassThrough(null, 200));

    private static SliceFilterContext MakeContext(string? clientIp, IServiceProvider? services = null) =>
        new(
            method: "POST",
            path: "/api/links/public",
            headers: EmptyHeaders,
            routeValues: EmptyRouteValues,
            services: services ?? new ServiceCollection().BuildServiceProvider(),
            clientIp: clientIp,
            cancellationToken: CancellationToken.None);

    // Each test creates its own RateLimitStore — no static state, no reset needed.
    // Tests are isolated by instance, not by serial execution.
    private static PublicCreateRateLimitFilter MakeFilter(FakeTimeProvider time) =>
        new(new RateLimitStore(), time, NullLogger<PublicCreateRateLimitFilter>.Instance);

    [Fact]
    public async Task First_request_passes_with_remaining_9()
    {
        var filter = MakeFilter(new FakeTimeProvider());
        var ctx = MakeContext("1.2.3.4");

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
        Assert.Equal("9", ctx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Tenth_request_passes_with_remaining_0()
    {
        var filter = MakeFilter(new FakeTimeProvider());

        var result = default(SliceFilterResult);
        var lastCtx = MakeContext("2.3.4.5");
        for (var i = 0; i < 10; i++)
        {
            lastCtx = MakeContext("2.3.4.5");
            result = await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.False(result.IsShortCircuit);
        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Eleventh_request_returns_429()
    {
        var filter = MakeFilter(new FakeTimeProvider());

        var result = default(SliceFilterResult);
        var lastCtx = MakeContext("3.4.5.6");
        for (var i = 0; i < 11; i++)
        {
            lastCtx = MakeContext("3.4.5.6");
            result = await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.True(result.IsShortCircuit);
        Assert.Equal("10", lastCtx.ResponseHeaders["X-RateLimit-Limit"]);
        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
        Assert.True(lastCtx.ResponseHeaders.TryGetValue("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!, CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Different_ips_have_independent_buckets()
    {
        var filter = MakeFilter(new FakeTimeProvider());

        // Exhaust IP A.
        for (var i = 0; i < 11; i++)
        {
            await filter.InvokeAsync(MakeContext("10.0.0.1"), PassThroughDelegate);
        }

        // IP B should still pass.
        var ctxB = MakeContext("10.0.0.2");
        var result = await filter.InvokeAsync(ctxB, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
    }

    [Fact]
    public async Task Window_reset_allows_new_requests()
    {
        var time = new FakeTimeProvider();
        var filter = MakeFilter(time);

        // Exhaust the rate limit.
        for (var i = 0; i < 11; i++)
        {
            await filter.InvokeAsync(MakeContext("5.6.7.8"), PassThroughDelegate);
        }

        // Advance the clock by more than the 1-minute window.
        time.Advance(TimeSpan.FromSeconds(61));

        // The next request should pass.
        var ctx = MakeContext("5.6.7.8");
        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
    }
}
