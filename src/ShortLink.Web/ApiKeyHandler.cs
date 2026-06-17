namespace ShortLink.Web;

/// <summary>
/// Attaches the X-Api-Key header to every outgoing API request.
/// Reads the key synchronously from <see cref="ApiKeyProvider"/> — no JS interop needed
/// (same pattern as BlazorSample's BearerTokenHandler).
/// When no key is configured, the header is omitted and the server returns 401.
/// </summary>
internal sealed class ApiKeyHandler(ApiKeyProvider provider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(provider.Current))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", provider.Current);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
