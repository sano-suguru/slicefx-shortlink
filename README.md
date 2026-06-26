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

## Production deployment

The app runs on Fly.io (nrt) with Neon (serverless Postgres). Two separate Fly apps:
- **API**: `slicefx-shortlink` — NativeAOT distroless container, port 8080
- **Web**: `slicefx-shortlink-web` — Blazor WASM served via nginx, port 80

### First-time setup

**Prerequisites**
- [flyctl](https://fly.io/docs/hands-on/install-flyctl/) installed (`brew install flyctl`)
- Fly.io account with billing set up: `fly auth login`
- [Neon](https://neon.tech) project created (recommended region: `ap-northeast-1` for nrt proximity)

**1. Create apps**
```bash
fly apps create slicefx-shortlink
fly apps create slicefx-shortlink-web
```

**2. Set API secrets** (from repo root)

> Use `DATABASE_URL` — not `ConnectionStrings__Postgres`. The URI-normalization path (`SslMode=Require` + percent-decode) only runs when `DATABASE_URL` is set. Using the keyword-format path with a `postgres://` URI will fail at startup.

```bash
fly secrets set \
  "DATABASE_URL=<Neon postgres:// URI>" \
  "SeedApiKey=$(openssl rand -hex 32)" \
  "BaseUrl=https://slicefx-shortlink.fly.dev"
```
Note the `SeedApiKey` value — it's the admin API key for the Web UI.

**3. Deploy** (always run from repo root — Dockerfiles use `COPY . .`)
```bash
fly deploy --remote-only
fly deploy --remote-only -c src/ShortLink.Web/fly.toml
```

**4. Smoke test**
```bash
BASE=https://slicefx-shortlink.fly.dev
KEY=<your SeedApiKey>

curl $BASE/health          # 200
curl $BASE/health/ready    # 200 (first hit may be slow — Neon cold start)

curl -X POST $BASE/api/links \
  -H "X-Api-Key: $KEY" -H "X-Request-Id: t1" -H "Content-Type: application/json" \
  -d '{"targetUrl":"https://example.com"}'   # 201, requestId echoed

CODE=<code from response>
curl -i $BASE/r/$CODE       # 302 → https://example.com
```

### Continuous deployment (GitHub Actions)

`deploy.yml` is `workflow_dispatch`-only. Set the `FLY_API_TOKEN` secret to an org-scoped token:

```bash
fly tokens create org personal -x 999999h   # covers all apps in the personal org
gh secret set FLY_API_TOKEN                 # paste the token
```

Then deploy from the Actions tab → **Deploy** → **Run workflow**.

### Operational notes

**Cold start latency.** Both Fly machines (`min_machines_running=0`) and Neon (serverless auto-suspend) scale to zero. The first request after idle triggers: Fly machine start → schema bootstrap against Neon → `/health/ready`. `grace_period=30s` is set to absorb Neon cold-start. Expect ~5–10s on the first hit.

**Rate limiting is in-memory per instance.** `RateLimitStore` is a singleton `ConcurrentDictionary`. With multiple running machines the effective limit becomes `Limit × machine_count`. This is an intentional tradeoff for this dogfooding context.

**SeedApiKey rotation.** `bootstrap` uses `ON CONFLICT DO NOTHING` — changing the key adds the new hash without removing the old one. To fully rotate:
1. `fly secrets set SeedApiKey=<new-key>` → redeploy
2. Delete the old row: `DELETE FROM api_keys WHERE label = 'seed' AND key_hash = '<old-hash>'`
   (old hash = `SELECT encode(sha256('<old-key>'::bytea), 'hex')`)

**Logs.** `fly logs` (distroless + NativeAOT — no exec/shell access). **Backup.** Neon free tier has 7-day PITR.

**Known accepted risks (dogfooding).** This is a single-user dogfooding deployment, not a hardened production service. Two risks are explicitly accepted:

1. **Structural open redirector.** `GET /r/{code}` unconditionally redirects to any http(s) URL minted by `POST /api/links/public`. Malicious public URLs (phishing/spam laundering) are not blocked — `CreateLinkRequestValidator` only guards against private/loopback hosts. An attacker can freely mint `https://<fly-domain>/r/xxx → https://phishing.example`. Accepted because the domain is an unnamed `fly.dev` subdomain with no brand value; monitor and mitigate if the domain gets blocklisted.

2. **localStorage API key.** The Web UI stores the API key in `localStorage`. XSS in the Blazor WASM app (or a compromised CDN asset) could exfiltrate the key and grant full CRUD over that owner's links. Accepted for single-user dogfooding.

## Structure

```
src/ShortLink.Api/        ASP.NET NativeAOT app
src/ShortLink.ApiClient/  Typed client (generated by slicefx client csharp)
tests/ShortLink.Api.Tests/  xUnit v3 integration tests (real Postgres)
docs/openapi.json         OpenAPI doc (generated by slicefx openapi)
.github/workflows/ci.yml  CI: build + test + AOT gate + Docker smoke
```
