using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Redirect;

[Feature("GET /r/{code}", Summary = "Follow a short link")]
[Filter<RateLimitFilter>]
public static class FollowLink
{
    public static async Task<SliceResult> Handle(
        string code,
        HttpContext http,
        ILinkStore links,
        IClickStore clicks,
        CancellationToken ct)
    {
        var link = await links.FindByCodeAsync(code, ct);
        if (link is null)
        {
            return SliceResult.NotFound();
        }

        var referer = http.Request.Headers.Referer.ToString();
        var userAgent = http.Request.Headers.UserAgent.ToString();
        await clicks.RecordAsync(
            link.Id,
            string.IsNullOrEmpty(referer) ? null : referer,
            string.IsNullOrEmpty(userAgent) ? null : userAgent,
            ct);

        return SliceResult.Redirect(link.TargetUrl);
    }
}
