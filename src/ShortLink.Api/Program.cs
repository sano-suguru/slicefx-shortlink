using Microsoft.AspNetCore.HttpOverrides;
using ShortLink.Api.Infrastructure;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);

var connStr = Db.ResolveConnectionString(builder.Configuration);
var dataSource = Db.Build(connStr);
builder.Services.AddSingleton(dataSource);
builder.Services.AddScoped<ILinkStore, PostgresLinkStore>();
builder.Services.AddScoped<IClickStore, PostgresClickStore>();
builder.Services.AddScoped<ICurrentApiKey, CurrentApiKey>();
builder.Services.AddScoped<ApiKeyValidator>();
builder.Services.AddSingleton<IShortLinkSettings>(new ShortLinkSettings
{
    BaseUrl = builder.Configuration["BaseUrl"]
        ?? throw new InvalidOperationException("BaseUrl is required. Set the BaseUrl environment variable."),
});

// CORS for the Blazor WASM admin UI (hosted separately on Fly.io).
// AllowAnyHeader is required so X-Api-Key passes the CORS preflight.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5201", "https://slicefx-shortlink-web.fly.dev")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Trust X-Forwarded-For from the single immediate upstream (Fly proxy).
// KnownNetworks/KnownProxies are cleared from their loopback-only defaults so the
// Fly proxy (non-loopback peer) is accepted. ForwardLimit=1 ensures only the last
// hop is trusted, preventing XFF spoofing from multi-hop chains.
var fwdOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
fwdOptions.KnownIPNetworks.Clear();
fwdOptions.KnownProxies.Clear();
app.UseForwardedHeaders(fwdOptions);

app.UseCors();

var seedKey = app.Configuration["SeedApiKey"]
    ?? throw new InvalidOperationException("SeedApiKey is required. Set the SeedApiKey environment variable.");
await Db.BootstrapAsync(dataSource, seedKey);

app.MapSlices();

app.Run();
