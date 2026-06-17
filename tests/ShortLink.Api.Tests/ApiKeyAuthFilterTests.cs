extern alias ShortLinkApi;
using Microsoft.Extensions.DependencyInjection;
using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Filters;
using ShortLinkApi::ShortLink.Api.Infrastructure;
using SliceFx;

namespace ShortLink.Api.Tests;

public sealed class ApiKeyAuthFilterTests : DbBackedTest
{
    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly SliceFilterDelegate PassThroughDelegate =
        _ => new ValueTask<SliceFilterResult>(SliceFilterResult.PassThrough(null, 200));

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Ds);
        services.AddSingleton<ApiKeyValidator>();
        services.AddScoped<ICurrentApiKey, CurrentApiKey>();
        return services.BuildServiceProvider();
    }

    private static SliceFilterContext MakeContext(
        IServiceProvider services,
        string? apiKey)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (apiKey is not null)
        {
            headers["X-Api-Key"] = apiKey;
        }

        return new SliceFilterContext(
            method: "POST",
            path: "/links",
            headers: headers,
            routeValues: EmptyRouteValues,
            services: services,
            clientIp: null,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Missing_api_key_returns_401()
    {
        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var filter = new ApiKeyAuthFilter(scope.ServiceProvider.GetRequiredService<ApiKeyValidator>());
        var ctx = MakeContext(scope.ServiceProvider, apiKey: null);

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.True(result.IsShortCircuit);
        Assert.Equal(401, result.Status);
    }

    [Fact]
    public async Task Empty_api_key_returns_401()
    {
        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var filter = new ApiKeyAuthFilter(scope.ServiceProvider.GetRequiredService<ApiKeyValidator>());
        var ctx = MakeContext(scope.ServiceProvider, apiKey: "");

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.True(result.IsShortCircuit);
        Assert.Equal(401, result.Status);
    }

    [Fact]
    public async Task Invalid_api_key_returns_401()
    {
        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var filter = new ApiKeyAuthFilter(scope.ServiceProvider.GetRequiredService<ApiKeyValidator>());
        var ctx = MakeContext(scope.ServiceProvider, apiKey: "not-a-real-key-xyz-123");

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.True(result.IsShortCircuit);
        Assert.Equal(401, result.Status);
    }

    [Fact]
    public async Task Valid_api_key_passes_through_and_sets_owner_id()
    {
        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var filter = new ApiKeyAuthFilter(scope.ServiceProvider.GetRequiredService<ApiKeyValidator>());
        var ctx = MakeContext(scope.ServiceProvider, apiKey: TestDb.SeedApiKey);

        var result = await filter.InvokeAsync(ctx, PassThroughDelegate);

        Assert.False(result.IsShortCircuit);

        var currentKey = scope.ServiceProvider.GetRequiredService<ICurrentApiKey>();
        Assert.Equal(ApiKeyValidator.Hash(TestDb.SeedApiKey), currentKey.OwnerId);
    }
}
