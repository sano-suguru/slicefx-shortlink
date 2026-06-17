using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShortLink.Api.Client;
using ShortLink.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ApiKeyProvider is singleton so all components share the same in-memory key.
builder.Services.AddSingleton<ApiKeyProvider>();
builder.Services.AddTransient<ApiKeyHandler>();

// Named HttpClient backed by the ApiKeyHandler DelegatingHandler.
// BaseAddress is loaded from wwwroot/appsettings[.Development].json.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl is required in wwwroot/appsettings.json.");

builder.Services.AddHttpClient(nameof(SliceApiClient), c => c.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<ApiKeyHandler>();

builder.Services.AddScoped(sp =>
    new SliceApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SliceApiClient))));

await builder.Build().RunAsync();
