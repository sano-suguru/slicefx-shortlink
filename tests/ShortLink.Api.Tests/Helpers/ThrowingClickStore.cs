extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests.Helpers;

/// <summary>
/// A hand-written stub that always throws from RecordAsync.
/// Used to verify that FollowLink swallows click-recording failures gracefully
/// without affecting the redirect response.
///
/// This is a stub (no verification of calls), not a mock — the only goal is
/// to force the failure path that real Postgres cannot reliably reproduce in tests.
/// Detroit/classicist: no mock framework, just a thin stub for a hard-to-reproduce failure.
/// </summary>
internal sealed class ThrowingClickStore : IClickStore
{
    public Task RecordAsync(long linkId, string? referer, string? userAgent, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated click-store failure for test purposes.");

    public Task<ClickStats> GetStatsAsync(long linkId, CancellationToken ct) =>
        Task.FromResult(new ClickStats(0, []));
}
