using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Links;

[Feature("GET /api/links", Summary = "List short links for the authenticated key")]
[SliceFilter<ApiKeyAuthFilter>]
public static class ListLinks
{
    public record Response(IReadOnlyList<LinkItem> Items, int Page, int PageSize);
    public record LinkItem(long Id, string Code, string ShortUrl, string TargetUrl, DateTimeOffset CreatedAt);

    public static async Task<SliceResult<Response>> Handle(
        int page,
        int pageSize,
        ICurrentApiKey key,
        ILinkStore links,
        IShortLinkSettings settings,
        CancellationToken ct)
    {
        if (page <= 0)
        {
            page = 1;
        }

        if (pageSize is <= 0 or > 100)
        {
            pageSize = 20;
        }

        var items = await links.ListByOwnerAsync(key.OwnerId!, page, pageSize, ct);
        var baseUrl = settings.BaseUrl;
        var mapped = items.Select(l => new LinkItem(l.Id, l.Code, $"{baseUrl}/r/{l.Code}", l.TargetUrl, l.CreatedAt))
                          .ToList();
        return SliceResult<Response>.Ok(new Response(mapped, page, pageSize));
    }
}
