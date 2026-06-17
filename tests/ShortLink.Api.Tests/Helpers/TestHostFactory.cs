extern alias ShortLinkApi;

using Microsoft.Extensions.DependencyInjection;
using SliceFx.Testing;

namespace ShortLink.Api.Tests.Helpers;

internal static class TestHostFactory
{
    static TestHostFactory()
    {
        // Ensure Postgres connection string is available for the test host
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")))
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestDb.ConnectionString);
        }

        // Default seed key and base URL for test host startup
        Environment.SetEnvironmentVariable("SeedApiKey", TestDb.SeedApiKey);
        Environment.SetEnvironmentVariable("BaseUrl", "http://localhost");
    }

    public static SliceTestHost<ShortLinkApi::Program> Create() =>
        SliceTestHost.Create<ShortLinkApi::Program>();

    /// <summary>
    /// Creates a test host with a custom DI configuration override.
    /// Use to swap out services with test doubles (e.g. ThrowingClickStore).
    /// </summary>
    public static SliceTestHost<ShortLinkApi::Program> Create(Action<IServiceCollection> configure) =>
        SliceTestHost.Create<ShortLinkApi::Program>(configure);
}
