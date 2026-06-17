extern alias ShortLinkApi;

namespace ShortLink.Api.Tests.Helpers;

/// <summary>
/// Abstract base for test classes that need a live Postgres connection.
/// Handles data source lifecycle and table truncation between tests.
/// </summary>
public abstract class DbBackedTest : IAsyncLifetime
{
    protected NpgsqlDataSource Ds { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Ds = TestDb.Build();
        await TestDb.ClearLinksAsync(Ds);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await Ds.DisposeAsync();
    }
}
