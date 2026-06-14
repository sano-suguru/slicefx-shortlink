namespace ShortLink.Api.Infrastructure;

public sealed record ClickStats(long TotalClicks, IReadOnlyList<DailyClicks> Daily);
public sealed record DailyClicks(DateOnly Date, long Count);

public interface IClickStore
{
    Task RecordAsync(long linkId, string? referer, string? userAgent, CancellationToken ct);
    Task<ClickStats> GetStatsAsync(long linkId, CancellationToken ct);
}
