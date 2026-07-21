extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class RetryTests
{
    private static Task NoSleep(TimeSpan _, CancellationToken __) => Task.CompletedTask;
    private static TimeSpan NoDelay(int _) => TimeSpan.Zero;

    [Fact]
    public async Task RunAsync_succeeds_on_first_attempt_calls_once()
    {
        var calls = 0;
        await Retry.RunAsync(_ => { calls++; return Task.CompletedTask; }, maxAttempts: 5, NoDelay, NoSleep, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunAsync_retries_then_succeeds()
    {
        var calls = 0;
        var sleeps = 0;
        await Retry.RunAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new InvalidOperationException("boom");
                }
                return Task.CompletedTask;
            },
            maxAttempts: 5,
            NoDelay,
            (_, __) => { sleeps++; return Task.CompletedTask; },
            TestContext.Current.CancellationToken);
        Assert.Equal(3, calls);
        Assert.Equal(2, sleeps);
    }

    [Fact]
    public async Task RunAsync_exhausts_and_throws_last_exception()
    {
        var calls = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.RunAsync(
                _ => { calls++; throw new InvalidOperationException($"fail {calls}"); },
                maxAttempts: 3,
                NoDelay,
                NoSleep,
                TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
        Assert.Equal("fail 3", ex.Message);
    }
}
