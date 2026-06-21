extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Filters;

namespace ShortLink.Api.Tests;

/// <summary>
/// Verifies that RateLimitStore.Consume never over-grants under parallel load.
/// Uses a fixed DateTimeOffset so that no window reset fires mid-test,
/// making the exact-count assertion deterministic (non-flaky).
/// </summary>
public sealed class RateLimitStoreConcurrencyTests
{
    [Fact]
    public async Task Consume_200_parallel_requests_allows_exactly_20()
    {
        var store = new RateLimitStore();
        // Fixed clock — same 'now' for all calls so no window reset fires during the test.
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        const int limit = 20;
        const int callers = 200;

        var tasks = Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
            store.Consume("test", "192.0.2.1", limit, TimeSpan.FromMinutes(1), now)));

        var results = await Task.WhenAll(tasks);

        var allowed = results.Count(r => r.Allowed);
        Assert.Equal(limit, allowed);
    }
}
