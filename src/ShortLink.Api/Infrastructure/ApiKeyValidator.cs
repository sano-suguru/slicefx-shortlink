using System.Security.Cryptography;
using Npgsql;

namespace ShortLink.Api.Infrastructure;

public sealed class ApiKeyValidator(NpgsqlDataSource db)
{
    public static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    // Returns the key hash (= OwnerId) when the key is valid, or null when invalid.
    // Returning the hash avoids a second Hash() call at the call site.
    public async Task<string?> ValidateAsync(string rawKey, CancellationToken ct)
    {
        var hash = Hash(rawKey);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key_hash FROM api_keys WHERE key_hash = $1";
        cmd.Parameters.AddWithValue(hash);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string ? hash : null;
    }
}
