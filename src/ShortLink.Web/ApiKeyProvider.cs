namespace ShortLink.Web;

/// <summary>
/// In-memory holder for the API key entered by the user.
/// Registered as singleton so all components share the same key.
/// localStorage sync is performed by the UI (Links.razor) on page load and on save.
/// WARNING: This is a dogfooding convenience — not a production security model.
/// </summary>
public sealed class ApiKeyProvider
{
    public string? Current { get; set; }
}
