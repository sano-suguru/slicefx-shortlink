namespace ShortLink.Api.Filters;

/// <summary>
/// Per-IP sliding-window rate limiter for the redirect endpoint (20 req/min).
/// </summary>
/// <inheritdoc cref="SlidingWindowRateLimitFilter"/>
public sealed class RateLimitFilter(IRateLimitStore store, TimeProvider time, ILogger<RateLimitFilter> logger)
    : SlidingWindowRateLimitFilter(store, time, logger)
{
    protected override int Limit => 20;
    protected override string Partition => "redirect";
}
