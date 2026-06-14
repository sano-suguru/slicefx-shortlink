extern alias ShortLinkApi;

using System.Net;
using System.Text.Json;
using ShortLink.Api.Tests.Helpers;

namespace ShortLink.Api.Tests;

public sealed class HealthTests : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await using var ds = TestDb.Build();
        await TestDb.ClearLinksAsync(ds);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetHealth_returns_200_with_ok_status()
    {
        await using var host = TestHostFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync("/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.TryGetProperty("at", out _));
    }
}
