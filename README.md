# slicefx-shortlink

URL shortener + click analytics, dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on the **ASP.NET Core NativeAOT** path.

Paired with [slicefx-inbox](https://github.com/sano-suguru/slicefx-inbox) which covers the WASI/Spin path.

## What this validates

| Concern | How |
|---|---|
| SliceFx NativeAOT source-gen dispatch | `[assembly: SliceAspNetAot]` + distroless container smoke |
| Scoped DI write-back via `ISliceFilter` | `ApiKeyAuthFilter` → `ICurrentApiKey` → handler |
| Host-neutral `ISliceFilter` (`ClientIp` + `ResponseHeaders`) | `RateLimitFilter` + `Retry-After` + `X-RateLimit-*` |
| Unauthenticated public feature + 2nd `ISliceFilter` rate limiter | `POST /api/links/public` → `PublicCreateRateLimitFilter` (10/min, shared singleton `RateLimitStore` / `"public-create"` partition) |
| Sentinel owner (no schema migration) | `AnonymousOwner.KeyHash = "anonymous"` — non-hex, never collides with SHA-256 owner hashes |
| `SliceResult.Redirect` in NativeAOT | `GET /r/{code}` → 302 |
| Raw Npgsql + NativeAOT trimming | `NpgsqlDataSourceBuilder`, no `EnableDynamicJson()` |
| `ISliceValidator<T>` under NativeAOT | `CreateLinkRequestValidator` auto-applies to both authed + public create (keyed on `CreateLinkRequest`) |
| `[FromHeader]` binding in `Handle` | `CreateLink` echoes `X-Request-Id` header via `SliceAotArgumentBinder` |
| Portability snapshot CI assertion | `slicefx routes --format json` checked per push |
| Typed client compilation | `slicefx client csharp` → `SliceApiClient.g.cs` |
| distroless container AOT gate | `docker run` curl smoke in CI |
| Liveness vs readiness separation | `GET /health` (liveness, no DB) + `GET /health/ready` (readiness, `SELECT 1`) wired to Fly health check |

## Portability snapshot

```
Links.CreateLink       portable   POST   /api/links
Links.CreatePublicLink portable   POST   /api/links/public
Links.ListLinks        portable   GET    /api/links
Links.DeleteLink       portable   DELETE /api/links/{id}
Links.GetLinkStats     portable   GET    /api/links/{id}/stats
Health.GetHealth       portable   GET    /health
Health.GetReady        portable   GET    /health/ready
Redirect.FollowLink    portable   GET    /r/{code}
```

All 8 routes are `portable`. Rate-limiting is implemented via `ISliceFilter` using `context.ClientIp` as the per-client bucket key and `context.ResponseHeaders` for `Retry-After`/`X-RateLimit-*`: `RateLimitFilter` (20/min) guards `/r/{code}`; `PublicCreateRateLimitFilter` (10/min) guards `POST /api/links/public`.

## Measured numbers (linux-x64, 2026-06-14)

| Metric | Value |
|---|---|
| Native binary | 18 MB |
| Container image (distroless chiseled) | 69.7 MB |

## Quick start

```bash
# 1. Start Postgres
docker compose up -d

# 2. Run locally (JIT, hot-reload friendly)
dotnet run --project src/ShortLink.Api

# 3. Use the API (seed key from appsettings.Development.json)
KEY=dev-seed-key-change-in-production
curl -s http://localhost:5200/health

curl -s -X POST http://localhost:5200/api/links \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $KEY" \
  -d '{"targetUrl":"https://example.com"}'
# -> {"id":1,"code":"KHxm26x","shortUrl":"http://localhost:5200/r/KHxm26x",...}

curl -i http://localhost:5200/r/KHxm26x          # 302 + Retry-After + X-RateLimit-*
curl -s http://localhost:5200/api/links/1/stats -H "X-Api-Key: $KEY"
```

## Local AOT gate

```bash
# IL2026/IL3050 surface here (no CI wait needed on macOS)
dotnet publish src/ShortLink.Api -c Release -r osx-arm64 -p:PublishAot=true
```

## Container smoke

```bash
docker build --platform linux/amd64 -f src/ShortLink.Api/Dockerfile -t shortlink-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;..." \
  -e SeedApiKey="your-key" \
  -e BaseUrl="http://localhost:8080" \
  shortlink-api
```

## SliceFx coverage & authoring notes

### Validated AOT paths

- **`ISliceFilter` with `ResponseHeaders` + `ClientIp`** — `RateLimitFilter` uses `context.ClientIp` as the per-client bucket key and writes `Retry-After`/`X-RateLimit-*` via `context.ResponseHeaders`. Works under NativeAOT, feature stays `portable`.
- **`ISliceValidator<T>`** — auto-discovered, runs DataAnnotations→Slice-validator→filter in order, zero IL warnings, feature stays `portable`. See `CreateLinkRequestValidator.cs`.
- **`[FromHeader]` in `Handle` signature** — `SliceAotArgumentBinder` correctly resolves header parameters (not confused with body binding, no SLICE070), feature stays `portable`. See `CreateLink.Handle` with `[FromHeader(Name="X-Request-Id")] string? requestId`.
- **`SliceResult.Redirect`** under NativeAOT — `GET /r/{code}` returns 302 + `Location` correctly via the AOT emitter's `SliceResultKind.Redirect` path.
- **Scoped DI write-back via `ISliceFilter`** — `ApiKeyAuthFilter` resolves the API key and writes to mutable scoped `CurrentApiKey`; handler reads it in the same request scope. Works correctly under NativeAOT.

### Authoring notes

- **SLICE070 disambiguation**: This app uses `[assembly: SliceAspNetAot]`, so the ASP.NET registration path uses compile-time binding — the same heuristic as WASI/Lambda portable dispatch. A concrete type that is **registered in `[SliceJsonContext(AspNet)]` and appears on a body verb (POST/PUT/PATCH)** becomes a second body candidate alongside the request DTO and triggers **SLICE070 (Error)**. Concretely: `NpgsqlDataSource` is *not* in the JSON context, so it is always resolved from DI regardless of verb (no diagnostic). Types that *are* in the JSON context on a POST/PUT/PATCH handler need `[FromServices]` or an interface type to avoid SLICE070. Fix: use interfaces for all DI services — interface/abstract types are always resolved from DI on all paths. `IConfiguration` is also problematic — wrap it in a user-defined settings interface (e.g. `IShortLinkSettings`).

## Deployment

Production runs on a free-tier stack, deployed automatically on push to `main`
by `.github/workflows/deploy.yml`:

- **API** — Render Web Service (Singapore), running a prebuilt NativeAOT image
  pulled from GHCR (`ghcr.io/sano-suguru/slicefx-shortlink-api`). Runtime config
  via environment variables: `DATABASE_URL`, `SeedApiKey`, `BaseUrl`,
  `CORS_ALLOWED_ORIGINS`, `PORT=8080`.
- **Web** — Cloudflare Pages (`slicefx-shortlink-web.pages.dev`), static Blazor
  WASM. SPA fallback via `wwwroot/_redirects`, cache policy via `wwwroot/_headers`.
- **DB** — Neon Postgres (unchanged).

See `docs/superpowers/specs/2026-07-10-render-pages-migration-design.md` for the
full design and cutover runbook.

## Known accepted risks (dogfooding)

This is a single-user dogfooding deployment, not a hardened production service. Two risks are explicitly accepted:

1. **Structural open redirector.** `GET /r/{code}` unconditionally redirects to any http(s) URL minted by `POST /api/links/public`. Malicious public URLs (phishing/spam laundering) are not blocked — `CreateLinkRequestValidator` only guards against private/loopback hosts. An attacker can freely mint `https://<render-domain>/r/xxx → https://phishing.example`. Accepted because the domain is an auto-assigned `slicefx-shortlink-api.onrender.com` subdomain with no brand value; monitor and mitigate if the domain gets blocklisted.

2. **localStorage API key.** The Web UI stores the API key in `localStorage`. XSS in the Blazor WASM app (or a compromised CDN asset) could exfiltrate the key and grant full CRUD over that owner's links. Accepted for single-user dogfooding.

## Structure

```
src/ShortLink.Api/        ASP.NET NativeAOT app
src/ShortLink.ApiClient/  Typed client (generated by slicefx client csharp)
tests/ShortLink.Api.Tests/  xUnit v3 integration tests (real Postgres)
docs/openapi.json         OpenAPI doc (generated by slicefx openapi)
.github/workflows/ci.yml  CI: build + test + AOT gate + Docker smoke
```
