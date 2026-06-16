using Npgsql;

namespace ShortLink.Api.Infrastructure;

public sealed class PostgresClickStore(NpgsqlDataSource db) : IClickStore
{
    public async Task RecordAsync(long linkId, string? referer, string? userAgent, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO clicks (link_id, referer, user_agent) VALUES ($1, $2, $3)";
        cmd.Parameters.AddWithValue(linkId);
        cmd.Parameters.AddWithValue((object?)referer ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ClickStats> GetStatsAsync(long linkId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        await using var totalCmd = conn.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM clicks WHERE link_id = $1";
        totalCmd.Parameters.AddWithValue(linkId);
        var total = (long)(await totalCmd.ExecuteScalarAsync(ct) ?? 0L);

        await using var dailyCmd = conn.CreateCommand();
        dailyCmd.CommandText = """
            SELECT (clicked_at AT TIME ZONE 'UTC')::date AS day, COUNT(*) AS cnt
            FROM clicks
            WHERE link_id = $1
            GROUP BY day
            ORDER BY day DESC
            LIMIT 30
            """;
        dailyCmd.Parameters.AddWithValue(linkId);
        await using var reader = await dailyCmd.ExecuteReaderAsync(ct);
        var daily = new List<DailyClicks>();
        while (await reader.ReadAsync(ct))
        {
            daily.Add(new DailyClicks(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetInt64(1)));
        }

        return new ClickStats(total, daily);
    }
}
