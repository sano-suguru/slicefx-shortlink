using ShortLink.Contracts;

namespace ShortLink.Api.Features.Health;

[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    public static Task<GetHealthResponse> Handle(TimeProvider clock) =>
        Task.FromResult(new GetHealthResponse("ok", clock.GetUtcNow()));
}
