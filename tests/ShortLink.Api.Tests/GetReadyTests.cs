extern alias ShortLinkApi;

using System.Net;
using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Features.Health;

namespace ShortLink.Api.Tests;

public sealed class GetReadyTests : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await using var ds = TestDb.Build();
        await TestDb.ClearLinksAsync(ds);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetReady_returns_200_when_db_up()
    {
        // Requires a running Postgres (same as all other integration tests).
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync("/health/ready", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetReady_returns_503_when_db_unreachable()
    {
        // Call Handle directly with a DataSource pointing to an unreachable host.
        // Port 1 is chosen because it is almost certainly not listening on any loopback address;
        // Timeout=1 and Command Timeout=1 (seconds) keep the test fast.
        var connStr = "Host=127.0.0.1;Port=1;Database=doesnotexist;Username=nobody;" +
                      "Password=nopassword;Timeout=1;Command Timeout=1;Ssl Mode=Prefer";
        await using var unreachableDs = NpgsqlDataSource.Create(connStr);
        var ct = TestContext.Current.CancellationToken;

        var result = await GetReady.Handle(unreachableDs, ct);

        // SliceResult.Problem(503, ...) sets StatusCode = 503
        Assert.Equal(503, result.Status);
    }
}
