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
    BaseUrl = builder.Configuration["BaseUrl"] ?? "http://localhost:5200",
});

var app = builder.Build();

// Trust X-Forwarded-For from the immediate upstream (reverse proxy / LB)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

var seedKey = app.Configuration["SeedApiKey"] ?? "dev-seed-key-change-in-production";
await Db.BootstrapAsync(dataSource, seedKey);

app.MapSlices();

app.Run();
