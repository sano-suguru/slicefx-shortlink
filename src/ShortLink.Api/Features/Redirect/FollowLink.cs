using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Redirect;

[Feature("GET /r/{code}", Summary = "Follow a short link")]
public static class FollowLink
{
    public static async Task<SliceResult> Handle(
        string code,
        ILinkStore links,
        IClickStore clicks,
        CancellationToken ct)
    {
        var link = await links.FindByCodeAsync(code, ct);
        if (link is null)
        {
            return SliceResult.NotFound();
        }

        await clicks.RecordAsync(link.Id, null, null, ct);
        return SliceResult.Redirect(link.TargetUrl);
    }
}
