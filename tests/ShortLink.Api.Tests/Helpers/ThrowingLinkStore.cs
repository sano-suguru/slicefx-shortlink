extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests.Helpers;

/// <summary>
/// A stub that always throws from every method.
/// Used to verify that endpoints respond with 500 (rather than 200/201/204)
/// when the link store is unavailable.
///
/// Detroit/classicist: no mock framework, just a thin stub for a hard-to-reproduce failure.
/// </summary>
internal sealed class ThrowingLinkStore : ILinkStore
{
    public Task<LinkRecord> CreateAsync(string targetUrl, string ownerKeyHash, string code, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");

    public Task<LinkRecord?> FindByCodeAsync(string code, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");

    public Task<LinkRecord?> FindByIdAsync(long id, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");

    public Task<IReadOnlyList<LinkRecord>> ListByOwnerAsync(string ownerKeyHash, int page, int pageSize, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");

    public Task<int> CountByOwnerAsync(string ownerKeyHash, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");

    public Task<bool> DeleteAsync(long id, string ownerKeyHash, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated link-store failure for test purposes.");
}
