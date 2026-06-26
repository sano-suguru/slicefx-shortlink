extern alias ShortLinkApi;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShortLinkApi::ShortLink.Api.Filters;
using SliceFx;

namespace ShortLink.Api.Tests;

public sealed class PublicCreateDailyGlobalRateLimitFilterTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly SliceFilterDelegate PassThroughDelegate =
        _ => new ValueTask<SliceFilterResult>(SliceFilterResult.PassThrough(null, 200));

    private static SliceFilterContext MakeContext(string? clientIp) =>
        new(
            method: "POST",
            path: "/api/links/public",
            headers: EmptyHeaders,
            routeValues: EmptyRouteValues,
            services: new ServiceCollection().BuildServiceProvider(),
            clientIp: clientIp,
            cancellationToken: CancellationToken.None);

    // Each test creates its own RateLimitStore — no static state, no reset needed.
    private static PublicCreateDailyGlobalRateLimitFilter MakeFilter(FakeTimeProvider time) =>
        new(new RateLimitStore(), time, NullLogger<PublicCreateDailyGlobalRateLimitFilter>.Instance);

    [Fact]
    public async Task First_request_passes_with_remaining_199()
    {
        var filter = MakeFilter(new FakeTimeProvider());
        var ctx = MakeContext("1.2.3.4");

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
        Assert.Equal("199", ctx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Two_hundredth_request_passes_with_remaining_0()
    {
        var filter = MakeFilter(new FakeTimeProvider());

        var lastCtx = MakeContext("2.3.4.5");
        for (var i = 0; i < 200; i++)
        {
            lastCtx = MakeContext("2.3.4.5");
            await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
    }

    [Fact]
    public async Task Two_hundred_first_request_returns_429()
    {
        var filter = MakeFilter(new FakeTimeProvider());

        var result = default(SliceFilterResult);
        var lastCtx = MakeContext("3.4.5.6");
        for (var i = 0; i < 201; i++)
        {
            lastCtx = MakeContext("3.4.5.6");
            result = await filter.InvokeAsync(lastCtx, PassThroughDelegate);
        }

        Assert.True(result.IsShortCircuit);
        Assert.Equal("200", lastCtx.ResponseHeaders["X-RateLimit-Limit"]);
        Assert.Equal("0", lastCtx.ResponseHeaders["X-RateLimit-Remaining"]);
        Assert.True(lastCtx.ResponseHeaders.TryGetValue("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!, CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task All_client_ips_share_one_bucket()
    {
        // The defining behavior: unlike per-IP filters, the global filter uses a single
        // shared bucket regardless of client IP. Exhausting from IP A blocks IP B too.
        var filter = MakeFilter(new FakeTimeProvider());

        // Exhaust the global quota from IP A.
        for (var i = 0; i < 201; i++)
        {
            await filter.InvokeAsync(MakeContext("10.0.0.1"), PassThroughDelegate);
        }

        // IP B is also blocked — global bucket is shared.
        var ctxB = MakeContext("10.0.0.2");
        var result = await filter.InvokeAsync(ctxB, PassThroughDelegate);

        Assert.True(result.IsShortCircuit);
        Assert.Equal(429, result.Status);
    }

    [Fact]
    public async Task Window_reset_allows_new_requests_after_one_day()
    {
        var time = new FakeTimeProvider();
        var filter = MakeFilter(time);

        // Exhaust the daily global quota.
        for (var i = 0; i < 201; i++)
        {
            await filter.InvokeAsync(MakeContext("5.6.7.8"), PassThroughDelegate);
        }

        // Advance the clock past the 1-day window.
        time.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(1)));

        // The next request should be allowed again.
        var ctx = MakeContext("5.6.7.8");
        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);
    }
}
