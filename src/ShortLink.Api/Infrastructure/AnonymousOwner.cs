namespace ShortLink.Api.Infrastructure;

/// <summary>
/// Sentinel owner for anonymously-created short links.
/// </summary>
/// <remarks>
/// Real owner hashes are produced by <see cref="ApiKeyValidator.Hash"/> — always 64 lower-case hex
/// characters ([0-9a-f]). This constant is intentionally non-hex so it can never collide with a
/// real owner hash. The <c>links.owner_key_hash</c> column remains <c>NOT NULL</c>; no schema
/// migration is required.
/// </remarks>
public static class AnonymousOwner
{
    /// <summary>
    /// The sentinel <c>owner_key_hash</c> value stored for links created without authentication.
    /// </summary>
    public const string KeyHash = "anonymous";
}
