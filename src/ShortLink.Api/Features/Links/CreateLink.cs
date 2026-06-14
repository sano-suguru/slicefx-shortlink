using System.ComponentModel.DataAnnotations;
using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Links;

[Feature("POST /api/links", Summary = "Create a short link")]
[SliceFilter<ApiKeyAuthFilter>]
public static class CreateLink
{
    public record Request(
        [Required, Url] string TargetUrl);

    public record Response(long Id, string Code, string ShortUrl, string TargetUrl, DateTimeOffset CreatedAt);

    public static async Task<SliceResult<Response>> Handle(
        Request req,
        ICurrentApiKey key,
        ILinkStore links,
        IShortLinkSettings settings,
        CancellationToken ct)
    {
        var code = ShortCode.Generate();
        var link = await links.CreateAsync(req.TargetUrl, key.OwnerId!, code, ct);

        var baseUrl = settings.BaseUrl;
        var resp = new Response(link.Id, link.Code, $"{baseUrl}/r/{link.Code}", link.TargetUrl, link.CreatedAt);
        return SliceResult<Response>.Created(resp, $"{baseUrl}/r/{link.Code}");
    }
}
