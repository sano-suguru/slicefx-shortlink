# CLAUDE.md — slicefx-shortlink

## What this is

URL shortener + click analytics app, dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on the **ASP.NET Core NativeAOT** path (the framework's primary target). Paired with `slicefx-inbox` which covers the WASI/Spin path.

Deploy target: distroless container (linux-x64 NativeAOT binary).
Backend: `src/ShortLink.Api/` — SliceFx ASP.NET NativeAOT app + Postgres (raw Npgsql).

## SliceFx reference rules

- **NuGet only — `<ProjectReference>` to slicefx/ is prohibited.**
- When a SliceFx bug is found: switch to `~/dev/slicefx` session → fix → `gh workflow run publish.yml` → bump `<PackageReference Version>` here.
- Current version: `0.1.0-preview.15`.

## Commands

```bash
# Build (non-AOT, fast iteration)
dotnet build ShortLink.slnx

# Run locally (JIT, port 5200)
dotnet run --project src/ShortLink.Api

# Local AOT gate — IL2026/IL3050 surface here even on macOS
dotnet publish src/ShortLink.Api -c Release -r osx-arm64 -p:PublishAot=true

# Portability check — all 7 routes should be portable
dotnet tool run slicefx -- routes --format table

# Docker build + smoke (real NativeAOT binary, linux-x64)
docker build --platform linux/amd64 -f src/ShortLink.Api/Dockerfile -t shortlink-api .
docker run --rm -p 8080:8080 shortlink-api
curl http://localhost:8080/health

# (M2+) With Postgres
docker compose up -d
# ConnectionStrings__Postgres="Host=localhost;Username=postgres;Password=postgres;Database=shortlink;SSL Mode=Prefer"

# (M4+) JSON context check
dotnet tool run slicefx -- json-context --check --project src/ShortLink.Api

# Regenerate OpenAPI document (run after changing feature request/response shapes)
dotnet tool run slicefx -- openapi --project src/ShortLink.Api --output docs/openapi.json --force

# Regenerate typed C# client (run after regenerating openapi.json)
dotnet tool run slicefx -- client csharp --project src/ShortLink.Api --output src/ShortLink.ApiClient/SliceApiClient.g.cs --namespace ShortLink.Api.Client --force
```

## Architecture

One-file-one-feature (SliceFx pattern). All 7 features are **portable** (`[SliceFilter<T>]` + `SliceResult<T>`). `GET /r/{code}` was aspnet-only before preview.15; now portable via `ISliceFilter` `RateLimitFilter` using `context.ClientIp` and `context.ResponseHeaders`.

```
src/ShortLink.Api/
  Program.cs                CreateSlimBuilder → AddSlice → services → MapSlices → Run
  AotSetup.cs               [assembly: SliceAspNetAot]
  AotJsonContext.cs         [SliceJsonContext(SliceJsonTarget.AspNet)] + all body/response roots
  Features/Health/
    GetHealth.cs            GET /health        (public, portable — liveness, no DB)
    GetReady.cs             GET /health/ready  (public, portable — readiness: SELECT 1)
  Features/Links/           (M2+)
    CreateLink.cs           POST /api/links     (API-key auth, portable)
    ListLinks.cs            GET  /api/links     (API-key auth, portable, paged)
    DeleteLink.cs           DELETE /api/links/{id}  (API-key auth, portable)
    GetLinkStats.cs         GET  /api/links/{id}/stats  (API-key auth, portable)
  Features/Redirect/        (M2+)
    FollowLink.cs           GET  /r/{code}  (public, portable — ISliceFilter RateLimitFilter)
  Filters/                  (M3+)
    ApiKeyAuthFilter.cs     ISliceFilter — portable; resolves key → CurrentApiKey write-back
    RateLimitFilter.cs      ISliceFilter — portable; context.ClientIp + context.ResponseHeaders
  Infrastructure/           (M2+)
    ILinkStore.cs / PostgresLinkStore.cs
    IClickStore.cs / PostgresClickStore.cs
    CurrentApiKey.cs        scoped mutable DI holder
    ApiKeyValidator.cs      SHA-256 hash compare (System.Security.Cryptography available in AOT)
    ShortCode.cs            base62 code generation
    Db.cs                   NpgsqlDataSource + schema.sql bootstrap + api_keys seed
  Db/schema.sql             CREATE TABLE IF NOT EXISTS links / clicks / api_keys
  Dockerfile                multi-stage distroless (AotSample pattern)
```

## Key constraints (NativeAOT)

- `PublishAot=true`, `InvariantGlobalization=true`, `TreatWarningsAsErrors=true` (IL2026/IL3050 = error).
- All body/response types must be registered in `AotJsonContext` — use `slicefx json-context --check`.
- `SliceTestHost` runs AOT-mode code under JIT (no trimming warnings). Only `dotnet publish` + `docker run` smoke is a real AOT gate.
- `[assembly: SliceAspNetAot]` in `AotSetup.cs` activates AOT-safe source-generated dispatch.
- **No `AddOpenApi()`** — use `slicefx openapi` instead (AOT path cannot infer Accepts/Produces).
- Npgsql: use `NpgsqlDataSourceBuilder` only. Do NOT call `EnableDynamicJson()` (AOT-unsafe).
- DB connection: `ConnectionStrings:Postgres` (Npgsql keyword format). Container env: `ConnectionStrings__Postgres`.
