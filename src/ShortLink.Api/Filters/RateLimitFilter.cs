using System.Collections.Concurrent;

namespace ShortLink.Api.Filters;

/// <summary>
/// In-memory, per-IP sliding-window rate limiter for the redirect endpoint.
/// <para>
/// <b>Single-instance / in-memory only.</b> When Fly.io auto-scales to multiple machines,
/// each machine enforces its own independent limit — the cluster-wide effective limit is
/// <c>Limit × machine count</c>, not <c>Limit</c>. For a scale-to-zero / single-machine
/// deployment this is the intended behaviour.
/// </para>
/// </summary>
public sealed class RateLimitFilter : ISliceFilter
{
    private const int Limit = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, Bucket> Buckets = new();

    // Periodic sweep: remove buckets whose window has fully expired to prevent unbounded growth.
    // One sweep per window interval; lock ensures a single sweep at a time.
    private static DateTimeOffset _nextSweep = DateTimeOffset.MinValue;
    private static readonly Lock _sweepLock = new();

    private static void SweepExpiredBuckets(DateTimeOffset now)
    {
        if (now < _nextSweep)
        {
            return;
        }

        lock (_sweepLock)
        {
            if (now < _nextSweep)
            {
                return;
            }

            _nextSweep = now.Add(Window);
            foreach (var key in Buckets.Keys.ToList())
            {
                if (Buckets.TryGetValue(key, out var b) && b.IsExpired(now))
                {
                    // R-2 note: there is a narrow TOCTOU window here — a concurrent Consume that
                    // rolls the window forward between IsExpired and TryRemove will silently lose
                    // its in-flight window consumption. Impact is at most one extra window of
                    // full-limit capacity for that IP; acceptable for a soft limiter.
                    Buckets.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>Clears all rate-limit buckets. For use in tests only.</summary>
    internal static void ResetForTests() => Buckets.Clear();

    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        var ip = context.ClientIp ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        SweepExpiredBuckets(now);

        var bucket = Buckets.GetOrAdd(ip, _ => new Bucket(Limit, now.Add(Window)));
        var remaining = bucket.Consume(Limit, now);

        if (remaining < 0)
        {
            var retryAfter = (int)Math.Ceiling((bucket.ResetAt - now).TotalSeconds);
            context.ResponseHeaders["Retry-After"] = retryAfter.ToString();
            context.ResponseHeaders["X-RateLimit-Limit"] = Limit.ToString();
            context.ResponseHeaders["X-RateLimit-Remaining"] = "0";
            return SliceFilterResult.ShortCircuit(SliceResult.Problem(429, "Too Many Requests"));
        }

        context.ResponseHeaders["X-RateLimit-Limit"] = Limit.ToString();
        context.ResponseHeaders["X-RateLimit-Remaining"] = remaining.ToString();

        return await next(context).ConfigureAwait(false);
    }

    private sealed class Bucket(int limit, DateTimeOffset resetAt)
    {
        private int _remaining = limit;

        public DateTimeOffset ResetAt { get; private set; } = resetAt;

        public int Consume(int limit, DateTimeOffset now)
        {
            lock (this)
            {
                if (now >= ResetAt)
                {
                    _remaining = limit - 1;
                    ResetAt = now.Add(Window);
                    return _remaining;
                }

                if (_remaining <= 0)
                {
                    return -1;
                }

                return --_remaining;
            }
        }

        // A bucket is safe to remove when its current window has fully elapsed.
        // If accessed again after removal, GetOrAdd will create a fresh bucket.
        public bool IsExpired(DateTimeOffset now)
        {
            lock (this)
            {
                return now >= ResetAt;
            }
        }
    }
}
