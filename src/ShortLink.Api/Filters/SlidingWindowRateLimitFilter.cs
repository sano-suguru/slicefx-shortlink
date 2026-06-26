namespace ShortLink.Api.Filters;

/// <summary>
/// Abstract base for in-memory, per-IP sliding-window rate limiters.
/// <para>
/// <b>Single-instance / in-memory only.</b> When Fly.io auto-scales to multiple machines,
/// each machine enforces its own independent limit — the cluster-wide effective limit is
/// <c>Limit × machine count</c>, not <c>Limit</c>. For a scale-to-zero / single-machine
/// deployment this is the intended behaviour.
/// </para>
/// <para>
/// Bucket state is held in <see cref="IRateLimitStore"/> (singleton), so rate-limit state is
/// isolated per host instance. Integration tests using <c>TestHostFactory.Create()</c> receive
/// a fresh store for every call — no manual resets or serial-execution constraints required.
/// </para>
/// <para>
/// IP resolution depends on <c>UseForwardedHeaders</c> configured in <c>Program.cs</c>. When
/// the client IP cannot be resolved, all requests fall into a shared <c>"unknown"</c> bucket
/// and a warning is logged so misconfigured proxy chains can be diagnosed.
/// </para>
/// </summary>
public abstract partial class SlidingWindowRateLimitFilter(
    IRateLimitStore store,
    TimeProvider time,
    ILogger logger) : ISliceFilter
{
    /// <summary>Maximum number of requests allowed per <see cref="Window"/> per IP.</summary>
    protected abstract int Limit { get; }

    /// <summary>Partition key that namespaces buckets across different rate-limit endpoints.</summary>
    protected abstract string Partition { get; }

    /// <summary>Length of each rate-limit window. Defaults to 1 minute.</summary>
    protected virtual TimeSpan Window => TimeSpan.FromMinutes(1);

    /// <summary>
    /// The bucket key used to partition rate-limit state within a <see cref="Partition"/>.
    /// Defaults to the client IP (or <c>"unknown"</c> when the IP cannot be resolved).
    /// Override to return a fixed string for a global (cross-IP) limit.
    /// </summary>
    protected virtual string GetBucketKey(string? clientIp) => clientIp ?? "unknown";

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RateLimit[{partition}]: ClientIp is null — UseForwardedHeaders may be " +
                  "misconfigured or the client connected directly. Falling back to 'unknown' bucket.")]
    private static partial void LogClientIpNull(ILogger logger, string partition);

    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        var ip = context.ClientIp;
        var clientKey = GetBucketKey(ip);

        // Warn only when the default per-IP bucket is used but the IP is unavailable.
        // Subclasses that return a fixed key (e.g. "global") don't need this warning.
        if (ip is null && clientKey == "unknown")
        {
            LogClientIpNull(logger, Partition);
        }
        var now = time.GetUtcNow();
        var decision = store.Consume(Partition, clientKey, Limit, Window, now);

        if (!decision.Allowed)
        {
            var retryAfter = (int)Math.Ceiling((decision.ResetAt - now).TotalSeconds);
            context.ResponseHeaders["Retry-After"] = retryAfter.ToString();
            context.ResponseHeaders["X-RateLimit-Limit"] = Limit.ToString();
            context.ResponseHeaders["X-RateLimit-Remaining"] = "0";
            return SliceFilterResult.ShortCircuit(SliceResult.Problem(429, "Too Many Requests"));
        }

        context.ResponseHeaders["X-RateLimit-Limit"] = Limit.ToString();
        context.ResponseHeaders["X-RateLimit-Remaining"] = decision.Remaining.ToString();

        return await next(context).ConfigureAwait(false);
    }
}
