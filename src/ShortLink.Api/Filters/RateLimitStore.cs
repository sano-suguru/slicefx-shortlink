using System.Collections.Concurrent;

namespace ShortLink.Api.Filters;

/// <summary>
/// Partition-aware, in-memory, per-key sliding-window rate-limit store.
/// Registered as a singleton so each host instance (production or test) gets independent
/// state — no static shared fields, no test-reset hooks required.
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Attempts to consume one token from the bucket identified by
    /// <paramref name="partition"/> and <paramref name="clientKey"/>.
    /// </summary>
    RateLimitDecision Consume(string partition, string clientKey, int limit, TimeSpan window, DateTimeOffset now);
}

/// <summary>The outcome of a rate-limit check.</summary>
/// <param name="Allowed">Whether the request is within the limit.</param>
/// <param name="Remaining">Tokens remaining after this call (0 when at limit).</param>
/// <param name="ResetAt">When the current window resets; used to compute <c>Retry-After</c>.</param>
public readonly record struct RateLimitDecision(bool Allowed, int Remaining, DateTimeOffset ResetAt);

public sealed class RateLimitStore : IRateLimitStore
{
    // Sweep cadence is a fixed constant, independent of per-partition window durations.
    // This decouples the sweep frequency from whatever window size callers pass, so that
    // adding a second partition with a different window won't silently change sweep behaviour.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;
    private readonly Lock _sweepLock = new();

    public RateLimitDecision Consume(string partition, string clientKey, int limit, TimeSpan window, DateTimeOffset now)
    {
        SweepExpiredBuckets(now);

        // NUL (\0) is used as a separator because it cannot appear in either a partition name
        // or an IP address, so redirect and public-create buckets can never collide.
        var key = $"{partition}\0{clientKey}";
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(limit, now.Add(window)));
        var (remaining, resetAt) = bucket.Consume(limit, window, now);
        return new RateLimitDecision(remaining >= 0, remaining >= 0 ? remaining : 0, resetAt);
    }

    private void SweepExpiredBuckets(DateTimeOffset now)
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

            _nextSweep = now.Add(SweepInterval);

            foreach (var key in _buckets.Keys.ToList())
            {
                if (_buckets.TryGetValue(key, out var b) && b.IsExpired(now))
                {
                    // R-2 note: narrow TOCTOU window — a concurrent Consume that rolls the window
                    // forward between IsExpired and TryRemove will silently lose its in-flight
                    // consumption. Impact: at most one extra window of full-limit capacity for that
                    // key; acceptable for a soft limiter.
                    _buckets.TryRemove(key, out _);
                }
            }
        }
    }

    private sealed class Bucket(int limit, DateTimeOffset resetAt)
    {
        private int _remaining = limit;

        public DateTimeOffset ResetAt { get; private set; } = resetAt;

        public (int Remaining, DateTimeOffset ResetAt) Consume(int limit, TimeSpan window, DateTimeOffset now)
        {
            lock (this)
            {
                if (now >= ResetAt)
                {
                    _remaining = limit - 1;
                    ResetAt = now.Add(window);
                    return (_remaining, ResetAt);
                }

                if (_remaining <= 0)
                {
                    return (-1, ResetAt);
                }

                return (--_remaining, ResetAt);
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (this)
            {
                return now >= ResetAt;
            }
        }
    }
}
