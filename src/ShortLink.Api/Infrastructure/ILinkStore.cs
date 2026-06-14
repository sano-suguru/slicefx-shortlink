namespace ShortLink.Api.Infrastructure;

public sealed record LinkRecord(long Id, string Code, string TargetUrl, string OwnerKeyHash, DateTimeOffset CreatedAt);

public interface ILinkStore
{
    Task<LinkRecord> CreateAsync(string targetUrl, string ownerKeyHash, string code, CancellationToken ct);
    Task<LinkRecord?> FindByCodeAsync(string code, CancellationToken ct);
    Task<LinkRecord?> FindByIdAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<LinkRecord>> ListByOwnerAsync(string ownerKeyHash, int page, int pageSize, CancellationToken ct);
    Task<bool> DeleteAsync(long id, string ownerKeyHash, CancellationToken ct);
}
