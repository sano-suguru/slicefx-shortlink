using ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Filters;

public sealed class ApiKeyAuthFilter(ApiKeyValidator validator) : ISliceFilter
{
    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        context.Headers.TryGetValue("X-Api-Key", out var rawKey);

        if (string.IsNullOrEmpty(rawKey))
        {
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("X-Api-Key header is required."));
        }

        var label = await validator.ValidateAsync(rawKey, context.CancellationToken).ConfigureAwait(false);
        if (label is null)
        {
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid API key."));
        }

        var current = context.Services.GetRequiredService<ICurrentApiKey>();
        current.OwnerId = ApiKeyValidator.Hash(rawKey);

        return await next(context).ConfigureAwait(false);
    }
}
