using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Links;

[Feature("GET /api/links/{id}/stats", Summary = "Get click stats for a short link")]
[SliceFilter<ApiKeyAuthFilter>]
public static class GetLinkStats
{
    public record Response(long Id, string Code, long TotalClicks, IReadOnlyList<DailyClicksItem> Daily);
    public record DailyClicksItem(DateOnly Date, long Count);

    public static async Task<SliceResult<Response>> Handle(
        long id,
        ICurrentApiKey key,
        ILinkStore links,
        IClickStore clicks,
        CancellationToken ct)
    {
        var link = await links.FindByIdAsync(id, ct);
        if (link is null || link.OwnerKeyHash != key.OwnerId)
        {
            return SliceResult<Response>.NotFound();
        }

        var stats = await clicks.GetStatsAsync(id, ct);
        var daily = stats.Daily.Select(d => new DailyClicksItem(d.Date, d.Count)).ToList();
        return SliceResult<Response>.Ok(new Response(link.Id, link.Code, stats.TotalClicks, daily));
    }
}
