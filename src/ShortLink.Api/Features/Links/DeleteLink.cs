using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Links;

[Feature("DELETE /api/links/{id}", Summary = "Delete a short link")]
[SliceFilter<ApiKeyAuthFilter>]
public static class DeleteLink
{
    public static async Task<SliceResult> Handle(
        long id,
        ICurrentApiKey key,
        ILinkStore links,
        CancellationToken ct)
    {
        var deleted = await links.DeleteAsync(id, key.OwnerId!, ct);
        return deleted ? SliceResult.NoContent() : SliceResult.NotFound();
    }
}
