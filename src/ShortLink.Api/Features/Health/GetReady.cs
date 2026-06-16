using Npgsql;

namespace ShortLink.Api.Features.Health;

[Feature("GET /health/ready", Summary = "Readiness probe — verifies DB is reachable")]
public static class GetReady
{
    public static async Task<SliceResult> Handle(NpgsqlDataSource db, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return SliceResult.Ok();
        }
        catch (Exception)
        {
            return SliceResult.Problem(503, "Service Unavailable", "Database is not reachable.");
        }
    }
}
