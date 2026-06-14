using ShortLink.Api.Infrastructure;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);

var connStr = Db.ResolveConnectionString(builder.Configuration);
var dataSource = Db.Build(connStr);
builder.Services.AddSingleton(dataSource);
builder.Services.AddScoped<ILinkStore, PostgresLinkStore>();
builder.Services.AddScoped<IClickStore, PostgresClickStore>();
builder.Services.AddScoped<CurrentApiKey>();
builder.Services.AddScoped<ApiKeyValidator>();

var app = builder.Build();

var seedKey = app.Configuration["SeedApiKey"] ?? "dev-seed-key-change-in-production";
await Db.BootstrapAsync(dataSource, seedKey);

app.MapSlices();

app.Run();
