using Microsoft.AspNetCore.HttpOverrides;
using ShortLink.Api.Filters;
using ShortLink.Api.Infrastructure;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRateLimitStore, RateLimitStore>();

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

// CORS for the Blazor WASM admin UI (hosted separately).
// AllowAnyHeader is required so X-Api-Key passes the CORS preflight.
// Allowed origins come from CORS_ALLOWED_ORIGINS (comma-separated); dev default is localhost.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(CorsOrigins.Parse(builder.Configuration["CORS_ALLOWED_ORIGINS"]))
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Trust X-Forwarded-For from the single immediate upstream reverse proxy
// (Render's load balancer in production; Fly previously). KnownNetworks/KnownProxies
// are cleared from their loopback-only defaults so the platform proxy (a non-loopback
// peer) is accepted. ForwardLimit=1 trusts only the last hop, preventing XFF spoofing
// from multi-hop chains. ClientIp (used for rate limiting) resolves from this.
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
// Retry bootstrap to absorb Neon cold-start on a Render wake: ~2+4+8+16+32s ≈ 62s
// across 6 attempts, comfortably under Render's 15-min deploy health window and above
// Neon's cold-start. Exhaustion rethrows — that indicates a real DB outage/misconfig.
await Retry.RunAsync(
    operation: token => Db.BootstrapAsync(dataSource, seedKey, token),
    maxAttempts: 6,
    delayFor: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
    sleep: Task.Delay);

app.MapSlices();

app.Run();
