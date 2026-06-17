using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;
using ShortLink.Contracts;

namespace ShortLink.Api.Features.Links;

[Feature("GET /api/links/{id}/stats", Summary = "Get click stats for a short link")]
[SliceFilter<ApiKeyAuthFilter>]
public static class GetLinkStats
{
    public static async Task<SliceResult<GetLinkStatsResponse>> Handle(
        long id,
        ICurrentApiKey key,
        ILinkStore links,
        IClickStore clicks,
        CancellationToken ct)
    {
        var link = await links.FindByIdAsync(id, ct);
        if (link is null || link.OwnerKeyHash != key.OwnerId)
        {
            return SliceResult<GetLinkStatsResponse>.NotFound();
        }

        var stats = await clicks.GetStatsAsync(id, ct);
        var daily = stats.Daily.Select(d => new DailyClicksItem(d.Date, d.Count)).ToList();
        return SliceResult<GetLinkStatsResponse>.Ok(new GetLinkStatsResponse(link.Id, link.Code, stats.TotalClicks, daily));
    }
}
