namespace ShortLink.Api.Filters;

/// <summary>
/// Per-IP sliding-window rate limiter for the anonymous create endpoint (10 req/min).
/// </summary>
/// <inheritdoc cref="SlidingWindowRateLimitFilter"/>
public sealed class PublicCreateRateLimitFilter(IRateLimitStore store, TimeProvider time, ILogger<PublicCreateRateLimitFilter> logger)
    : SlidingWindowRateLimitFilter(store, time, logger)
{
    protected override int Limit => 10;
    protected override string Partition => "public-create";
}
