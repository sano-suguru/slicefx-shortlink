using System.ComponentModel.DataAnnotations;

namespace ShortLink.Contracts;

// GetHealth
public record GetHealthResponse(string Status, DateTimeOffset At);

// CreateLink
// CreateLinkRequest uses explicit mutable properties (not positional record) so that
// Blazor EditForm two-way binding via @bind-Value works. Source generator reads
// DataAnnotations from properties too, so validation generation is unaffected.
public sealed class CreateLinkRequest
{
    [Required, Url]
    public string TargetUrl { get; set; } = "";
}

// RequestId: echo of X-Request-Id header. Maintained because LinkTests and ci.yml smoke
// depend on [FromHeader] binding verification.
public record CreateLinkResponse(
    long Id,
    string Code,
    string ShortUrl,
    string TargetUrl,
    DateTimeOffset CreatedAt,
    string? RequestId = null);

// ListLinks
public record ListLinksResponse(
    IReadOnlyList<LinkItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record LinkItem(
    long Id,
    string Code,
    string ShortUrl,
    string TargetUrl,
    DateTimeOffset CreatedAt);

// GetLinkStats — DateOnly requires JSON source gen to be verified in WASM context
public record GetLinkStatsResponse(
    long Id,
    string Code,
    long TotalClicks,
    IReadOnlyList<DailyClicksItem> Daily);

public record DailyClicksItem(DateOnly Date, long Count);
