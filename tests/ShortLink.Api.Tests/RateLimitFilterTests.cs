extern alias ShortLinkApi;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ShortLinkApi::ShortLink.Api.Filters;
using SliceFx;

namespace ShortLink.Api.Tests;

public sealed class RateLimitFilterTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly SliceFilterDelegate PassThroughDelegate =
        _ => new ValueTask<SliceFilterResult>(SliceFilterResult.PassThrough(null, 200));

    private static SliceFilterContext MakeContext(string? clientIp, IServiceProvider? services = null) =>
        new(
            method: "GET",
            path: "/r/ABC123",
            headers: EmptyHeaders,
            routeValues: EmptyRouteValues,
            services: services ?? new ServiceCollection().BuildServiceProvider(),
            clientIp: clientIp,
            cancellationToken: CancellationToken.None);

    [Fact]
    public async Task First_request_passes_with_remaining_19()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);
        var ctx = MakeContext("1.2.3.4");

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
        Assert.Equal("19", ctx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Twentieth_request_passes_with_remaining_0()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);

        var result = default(SliceFilterResult);
        var lastCtx = MakeContext("2.3.4.5");
        for (var i = 0; i < 20; i++)
        {
            lastCtx = MakeContext("2.3.4.5");
            result = await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.False(result.IsShortCircuit);
        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task TwentyFirst_request_returns_429()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);

        var result = default(SliceFilterResult);
        var lastCtx = MakeContext("3.4.5.6");
        for (var i = 0; i < 21; i++)
        {
            lastCtx = MakeContext("3.4.5.6");
            result = await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.True(result.IsShortCircuit);
        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
        Assert.True(lastCtx.ResponseHeaders.TryGetValue("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!, CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Different_ips_have_independent_buckets()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);

        // Exhaust IP A.
        for (var i = 0; i < 21; i++)
        {
            await filter.InvokeAsync(MakeContext("10.0.0.1"), PassThroughDelegate);
        }

        // IP B should still pass.
        var ctxB = MakeContext("10.0.0.2");
        var result = await filter.InvokeAsync(ctxB, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
    }

    [Fact]
    public async Task Null_clientip_uses_unknown_bucket()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);

        SliceFilterResult result = default;
        for (var i = 0; i < 21; i++)
        {
            result = await filter.InvokeAsync(MakeContext(clientIp: null), PassThroughDelegate);
        }

        Assert.True(result.IsShortCircuit);
    }

    [Fact]
    public async Task Window_reset_allows_new_requests()
    {
        RateLimitFilter.ResetForTests();
        var time = new FakeTimeProvider();
        var filter = new RateLimitFilter(time);

        // Exhaust the rate limit.
        for (var i = 0; i < 21; i++)
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
