using System.Collections.Concurrent;

namespace ShortLink.Api.Filters;

public sealed class RateLimitFilter : IEndpointFilter
{
    private const int Limit = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, Bucket> Buckets = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        var bucket = Buckets.GetOrAdd(ip, _ => new Bucket(Limit, now.Add(Window)));
        var remaining = bucket.Consume(Limit, now);

        if (remaining < 0)
        {
            // Short-circuit: set all headers before return (response not yet started)
            var retryAfter = (int)Math.Ceiling((bucket.ResetAt - now).TotalSeconds);
            http.Response.Headers["Retry-After"] = retryAfter.ToString();
            http.Response.Headers["X-RateLimit-Limit"] = Limit.ToString();
            http.Response.Headers["X-RateLimit-Remaining"] = "0";
            return Results.StatusCode(429);
        }

        // Set X-RateLimit-* BEFORE next() — post-handler header mutation is not possible in AOT
        http.Response.Headers["X-RateLimit-Limit"] = Limit.ToString();
        http.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();

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
