namespace ShortLink.Api.Infrastructure;

/// <summary>
/// Parses the <c>CORS_ALLOWED_ORIGINS</c> environment variable (comma-separated).
/// Uses only string operations so it is safe under NativeAOT (no reflection binder).
/// </summary>
public static class CorsOrigins
{
    private static readonly string[] DevDefault = ["http://localhost:5201"];

    public static string[] Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DevDefault;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
