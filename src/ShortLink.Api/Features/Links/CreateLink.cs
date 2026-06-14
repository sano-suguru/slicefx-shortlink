using System.ComponentModel.DataAnnotations;
using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Features.Links;

[Feature("POST /api/links", Summary = "Create a short link")]
public static class CreateLink
{
    public record Request(
        [Required, Url] string TargetUrl);

    public record Response(long Id, string Code, string ShortUrl, string TargetUrl, DateTimeOffset CreatedAt);

    public static async Task<SliceResult<Response>> Handle(
        Request req,
        HttpContext http,
        ILinkStore links,
        CancellationToken ct)
    {
        var rawKey = http.Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(rawKey))
        {
            return SliceResult<Response>.Unauthorized("X-Api-Key header is required.");
        }

        var ownerKeyHash = ApiKeyValidator.Hash(rawKey);
        var code = ShortCode.Generate();
        var link = await links.CreateAsync(req.TargetUrl, ownerKeyHash, code, ct);

        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        var resp = new Response(link.Id, link.Code, $"{baseUrl}/r/{link.Code}", link.TargetUrl, link.CreatedAt);
        return SliceResult<Response>.Created(resp, $"{baseUrl}/r/{link.Code}");
    }
}
