namespace ShortLink.Api.Filters;

/// <summary>
/// Daily global cap on anonymous link creation (all IPs combined: 200 req/day).
/// Runs <em>after</em> <see cref="PublicCreateRateLimitFilter"/> in declaration order.
/// <para>
/// This supplements the per-IP 10/min limit. IP rotation can bypass per-IP limits,
/// but every anonymous create still counts against this shared daily bucket.
/// </para>
/// <para>
/// <b>Trade-off (accepted):</b> a single attacker can exhaust the daily quota and
/// deny anonymous creation to legitimate visitors. Acceptable for single-user
/// dogfooding where anonymous create is a low-volume, best-effort feature.
/// See README "Known accepted risks" for context.
/// </para>
/// <inheritdoc cref="SlidingWindowRateLimitFilter"/>
/// </summary>
public sealed class PublicCreateDailyGlobalRateLimitFilter(IRateLimitStore store, TimeProvider time, ILogger<PublicCreateDailyGlobalRateLimitFilter> logger)
    : SlidingWindowRateLimitFilter(store, time, logger)
{
    protected override int Limit => 200;
    protected override string Partition => "public-create-global";
    protected override TimeSpan Window => TimeSpan.FromDays(1);

    // All requests share one global bucket regardless of client IP.
    protected override string GetBucketKey(string? clientIp) => "global";
}
