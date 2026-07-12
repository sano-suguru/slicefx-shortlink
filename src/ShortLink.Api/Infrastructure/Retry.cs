namespace ShortLink.Api.Infrastructure;

/// <summary>
/// Minimal async retry with caller-supplied backoff and sleep, so the delay policy
/// can be injected (real <see cref="Task.Delay(TimeSpan, CancellationToken)"/> in
/// production, a no-op in tests). Uses no reflection (AOT-safe).
/// </summary>
public static class Retry
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> operation,
        int maxAttempts,
        Func<int, TimeSpan> delayFor,
        Func<TimeSpan, CancellationToken, Task> sleep,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation(ct);
                return;
            }
            catch when (attempt < maxAttempts)
            {
                await sleep(delayFor(attempt), ct);
            }
        }
    }
}
