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

        // ValidateAsync returns the hash (= OwnerId) on success, null on failure.
        var ownerHash = await validator.ValidateAsync(rawKey, context.CancellationToken).ConfigureAwait(false);
        if (ownerHash is null)
        {
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid API key."));
        }

        var current = context.Services.GetRequiredService<ICurrentApiKey>();
        current.OwnerId = ownerHash;

        return await next(context).ConfigureAwait(false);
    }
}
