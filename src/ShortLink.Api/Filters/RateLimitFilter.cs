using System.Collections.Concurrent;

namespace ShortLink.Api.Filters;

public sealed class RateLimitFilter : ISliceFilter
{
    private const int Limit = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, Bucket> Buckets = new();

    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        var ip = context.ClientIp ?? "unknown";
        var now = DateTimeOffset.UtcNow;

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
    }
}
