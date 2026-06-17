using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;
using ShortLink.Contracts;

namespace ShortLink.Api.Features.Links;

[Feature("GET /api/links", Summary = "List short links for the authenticated key")]
[SliceFilter<ApiKeyAuthFilter>]
public static class ListLinks
{
    public static async Task<SliceResult<ListLinksResponse>> Handle(
        int? page,
        int? pageSize,
        ICurrentApiKey key,
        ILinkStore links,
        IShortLinkSettings settings,
        CancellationToken ct)
    {
        // Nullable params so query string is optional (omitted → defaults applied below).
        // Page is clamped to [1, 10_000] to prevent int overflow in the OFFSET calculation.
        var p = Math.Clamp(page ?? 1, 1, 10_000);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var itemsTask = links.ListByOwnerAsync(key.OwnerId!, p, ps, ct);
        var totalTask = links.CountByOwnerAsync(key.OwnerId!, ct);
        await Task.WhenAll(itemsTask, totalTask);
        var items = itemsTask.Result;
        var total = totalTask.Result;
        var baseUrl = settings.BaseUrl;
        var mapped = items.Select(l => new LinkItem(l.Id, l.Code, $"{baseUrl}/r/{l.Code}", l.TargetUrl, l.CreatedAt))
                          .ToList();
        return SliceResult<ListLinksResponse>.Ok(new ListLinksResponse(mapped, total, p, ps));
    }
}
