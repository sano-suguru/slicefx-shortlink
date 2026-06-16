using Npgsql;

namespace ShortLink.Api.Infrastructure;

public sealed class PostgresLinkStore(NpgsqlDataSource db) : ILinkStore
{
    private const int MaxRetries = 5;

    public async Task<LinkRecord> CreateAsync(string targetUrl, string ownerKeyHash, string code, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var currentCode = attempt == 0 ? code : ShortCode.Generate();
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO links (code, target_url, owner_key_hash)
                    VALUES ($1, $2, $3)
                    RETURNING id, code, target_url, owner_key_hash, created_at
                    """;
                cmd.Parameters.AddWithValue(currentCode);
                cmd.Parameters.AddWithValue(targetUrl);
                cmd.Parameters.AddWithValue(ownerKeyHash);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                return ReadLink(reader);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // unique violation on code — retry with new code
            }
        }
        throw new InvalidOperationException("Failed to generate a unique short code after retries.");
    }

    public async Task<LinkRecord?> FindByCodeAsync(string code, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, target_url, owner_key_hash, created_at FROM links WHERE code = $1";
        cmd.Parameters.AddWithValue(code);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadLink(reader) : null;
    }

    public async Task<LinkRecord?> FindByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, target_url, owner_key_hash, created_at FROM links WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadLink(reader) : null;
    }

    public async Task<IReadOnlyList<LinkRecord>> ListByOwnerAsync(string ownerKeyHash, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, target_url, owner_key_hash, created_at
            FROM links
            WHERE owner_key_hash = $1
            ORDER BY created_at DESC
            LIMIT $2 OFFSET $3
            """;
        cmd.Parameters.AddWithValue(ownerKeyHash);
        cmd.Parameters.AddWithValue(pageSize);
        // Cast to long before multiplication to prevent int overflow for large page values.
        cmd.Parameters.AddWithValue((long)(page - 1) * pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<LinkRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadLink(reader));
        }
        return results;
    }

    public async Task<int> CountByOwnerAsync(string ownerKeyHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM links WHERE owner_key_hash = $1";
        cmd.Parameters.AddWithValue(ownerKeyHash);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    public async Task<bool> DeleteAsync(long id, string ownerKeyHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM links WHERE id = $1 AND owner_key_hash = $2";
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(ownerKeyHash);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static LinkRecord ReadLink(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
            new DateTimeOffset(r.GetDateTime(4), TimeSpan.Zero));
}
