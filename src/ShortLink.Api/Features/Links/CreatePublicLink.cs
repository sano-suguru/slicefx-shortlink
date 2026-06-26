using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;
using ShortLink.Contracts;

namespace ShortLink.Api.Features.Links;

[Feature("POST /api/links/public", Summary = "Create a short link anonymously")]
[SliceFilter<PublicCreateRateLimitFilter>]
[SliceFilter<PublicCreateDailyGlobalRateLimitFilter>]
public static class CreatePublicLink
{
    public static async Task<SliceResult<CreatePublicLinkResponse>> Handle(
        CreateLinkRequest req,
        ILinkStore links,
        IShortLinkSettings settings,
        CancellationToken ct)
    {
        var code = ShortCode.Generate();
        var link = await links.CreateAsync(req.TargetUrl, AnonymousOwner.KeyHash, code, ct);

        var baseUrl = settings.BaseUrl;
        var resp = new CreatePublicLinkResponse(link.Id, link.Code, $"{baseUrl}/r/{link.Code}", link.TargetUrl, link.CreatedAt);
        return SliceResult<CreatePublicLinkResponse>.Created(resp, $"{baseUrl}/r/{link.Code}");
    }
}
