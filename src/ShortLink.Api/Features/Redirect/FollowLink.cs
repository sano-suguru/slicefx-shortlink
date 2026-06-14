using Microsoft.AspNetCore.Mvc;
using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Redirect;

[Feature("GET /r/{code}", Summary = "Follow a short link")]
[SliceFilter<RateLimitFilter>]
public static class FollowLink
{
    public static async Task<SliceResult> Handle(
        string code,
        [FromHeader(Name = "Referer")] string? referer,
        [FromHeader(Name = "User-Agent")] string? userAgent,
        ILinkStore links,
        IClickStore clicks,
        CancellationToken ct)
    {
        var link = await links.FindByCodeAsync(code, ct);
        if (link is null)
        {
            return SliceResult.NotFound();
        }

        await clicks.RecordAsync(
            link.Id,
            string.IsNullOrEmpty(referer) ? null : referer,
            string.IsNullOrEmpty(userAgent) ? null : userAgent,
            ct);

        return SliceResult.Redirect(link.TargetUrl);
    }
}
