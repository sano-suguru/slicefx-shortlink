# Render + Cloudflare Pages + Neon Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `slicefx-shortlink` production off Fly.io (free tier ended) to a no-credit-card free stack — API on Render (prebuilt GHCR NativeAOT image), Web on Cloudflare Pages, DB unchanged on Neon — deployed by GitHub Actions on push to `main`.

**Architecture:** GitHub Actions builds the NativeAOT API image and pushes it to GHCR, then triggers a sha-pinned Render deploy hook; a parallel job publishes the Blazor WASM and deploys it to Cloudflare Pages via wrangler. The API reads all environment-specific values (DB URL, CORS origins, base URL) from environment variables. Neon Postgres is reused as-is.

**Tech Stack:** .NET 10 (NativeAOT), Blazor WebAssembly, Npgsql, Docker/buildx, GitHub Actions, Render, Cloudflare Pages/wrangler, Neon Postgres.

## Global Constraints

- Target framework `net10.0`; SDK pinned by `global.json` to `10.0.300`.
- API project is `PublishAot=true`. **No reflection-based configuration binding** (`IConfiguration.Get<T>()` / `.Get<string[]>()`) — it emits IL2026/IL3050 and fails the AOT gate.
- `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `Nullable=enable`, `ImplicitUsings=enable`. Any warning is a build failure.
- Test project references `ShortLink.Api` via `extern alias ShortLinkApi`; unit tests start with `extern alias ShortLinkApi;` and `using ShortLinkApi::ShortLink.Api.Infrastructure;`. xUnit v3. Test methods use `_` separators (CA1707 suppressed in the test csproj).
- Tests that touch the DB require a local Postgres (`docker compose up -d`, connection `Host=localhost;Username=postgres;Password=postgres;Database=shortlink`). Pure unit tests below require no DB.
- Deployment defaults: API host `slicefx-shortlink-api.onrender.com` (Render, Singapore region), Web host `slicefx-shortlink-web.pages.dev` (Cloudflare Pages). GHCR image `ghcr.io/sano-suguru/slicefx-shortlink-api`, published **public** (no secrets baked in; all secrets are runtime env).
- `BaseUrl` must exactly equal the API's public origin (it is the base of 302 redirect `Location` and generated short URLs).

---

## File Structure

**API code (Task 1–4):**
- Modify `src/ShortLink.Api/Infrastructure/Db.cs` — add connection `Timeout=30` in `NormalizeUri`.
- Create `src/ShortLink.Api/Infrastructure/CorsOrigins.cs` — AOT-safe parse of comma-separated origins.
- Create `src/ShortLink.Api/Infrastructure/Retry.cs` — generic async retry helper.
- Modify `src/ShortLink.Api/Program.cs` — CORS from env, host-neutral forwarded-headers comment, bootstrap wrapped in retry.
- Tests: `tests/ShortLink.Api.Tests/DbTests.cs` (add), `tests/ShortLink.Api.Tests/CorsOriginsTests.cs` (create), `tests/ShortLink.Api.Tests/RetryTests.cs` (create).

**Web / Pages (Task 5):**
- Create `src/ShortLink.Web/wwwroot/_redirects`, `src/ShortLink.Web/wwwroot/_headers`.
- Modify `src/ShortLink.Web/ShortLink.Web.csproj` — disable build-time compression.
- Modify `src/ShortLink.Web/wwwroot/appsettings.json` — `ApiBaseUrl` → Render URL.

**CI/CD (Task 6):**
- Rewrite `.github/workflows/deploy.yml`.

**Docs & cleanup (Task 7, 10):**
- Modify `README.md`, `CLAUDE.md` deploy sections.
- Remove `fly.toml`, `src/ShortLink.Web/fly.toml`, `src/ShortLink.Web/Dockerfile`, `src/ShortLink.Web/nginx.conf` (post-cutover).

**Manual / ops (Task 8, 9):** console setup + cutover (no code; checklists with exact commands).

All code tasks are developed on branch `feat/migrate-render-pages`. Merging to `main` is the cutover trigger and MUST come after console setup (Task 8).

---

### Task 1: Connection timeout in NormalizeUri

**Files:**
- Modify: `src/ShortLink.Api/Infrastructure/Db.cs:60-68`
- Test: `tests/ShortLink.Api.Tests/DbTests.cs`

**Interfaces:**
- Consumes: existing `Db.ResolveConnectionString(IConfiguration)` (public), which calls private `NormalizeUri`.
- Produces: no signature change; the connection string emitted for `DATABASE_URL` inputs now contains `Timeout=30`.

- [ ] **Step 1: Write the failing test**

Add to `tests/ShortLink.Api.Tests/DbTests.cs` (same class `DbTests`, uses existing `BuildConfig` helper):

```csharp
[Fact]
public void ResolveConnectionString_sets_connection_timeout_for_DATABASE_URL()
{
    var config = BuildConfig(new Dictionary<string, string?>
    {
        ["DATABASE_URL"] = "postgres://user:pass@db.example.com:5432/mydb",
    });

    var result = Db.ResolveConnectionString(config);

    Assert.Contains("Timeout=30", result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~ResolveConnectionString_sets_connection_timeout_for_DATABASE_URL"`
Expected: FAIL — `Timeout=30` not present (default builder omits it).

- [ ] **Step 3: Add the timeout to NormalizeUri**

In `src/ShortLink.Api/Infrastructure/Db.cs`, add `Timeout = 30,` to the `NpgsqlConnectionStringBuilder` initializer:

```csharp
var b = new NpgsqlConnectionStringBuilder
{
    Host = u.Host,
    Port = u.Port > 0 ? u.Port : 5432,
    Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : null,
    Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
    Database = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')),
    SslMode = SslMode.Require,
    Timeout = 30,
};
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~ResolveConnectionString_sets_connection_timeout_for_DATABASE_URL"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ShortLink.Api/Infrastructure/Db.cs tests/ShortLink.Api.Tests/DbTests.cs
git commit -m "feat(api): set Npgsql connection Timeout=30 for Neon cold-start tolerance"
```

---

### Task 2: CorsOrigins.Parse helper (AOT-safe)

**Files:**
- Create: `src/ShortLink.Api/Infrastructure/CorsOrigins.cs`
- Test: `tests/ShortLink.Api.Tests/CorsOriginsTests.cs`

**Interfaces:**
- Produces: `public static string[] CorsOrigins.Parse(string? raw)` — returns `["http://localhost:5201"]` when `raw` is null/empty/whitespace; otherwise the comma-split, trimmed, empty-entry-free list. No reflection (AOT-safe).

- [ ] **Step 1: Write the failing test**

Create `tests/ShortLink.Api.Tests/CorsOriginsTests.cs`:

```csharp
extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class CorsOriginsTests
{
    [Fact]
    public void Parse_null_returns_dev_default()
        => Assert.Equal(new[] { "http://localhost:5201" }, CorsOrigins.Parse(null));

    [Fact]
    public void Parse_empty_returns_dev_default()
        => Assert.Equal(new[] { "http://localhost:5201" }, CorsOrigins.Parse("   "));

    [Fact]
    public void Parse_single_origin()
        => Assert.Equal(new[] { "https://x.pages.dev" }, CorsOrigins.Parse("https://x.pages.dev"));

    [Fact]
    public void Parse_multiple_trims_and_drops_empties()
        => Assert.Equal(
            new[] { "https://x.pages.dev", "http://localhost:5201" },
            CorsOrigins.Parse(" https://x.pages.dev , , http://localhost:5201 "));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~CorsOriginsTests"`
Expected: FAIL — `CorsOrigins` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

Create `src/ShortLink.Api/Infrastructure/CorsOrigins.cs`:

```csharp
namespace ShortLink.Api.Infrastructure;

/// <summary>
/// Parses the <c>CORS_ALLOWED_ORIGINS</c> environment variable (comma-separated).
/// Uses only string operations so it is safe under NativeAOT (no reflection binder).
/// </summary>
public static class CorsOrigins
{
    private static readonly string[] DevDefault = ["http://localhost:5201"];

    public static string[] Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DevDefault;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~CorsOriginsTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ShortLink.Api/Infrastructure/CorsOrigins.cs tests/ShortLink.Api.Tests/CorsOriginsTests.cs
git commit -m "feat(api): AOT-safe CORS_ALLOWED_ORIGINS parser"
```

---

### Task 3: Retry helper

**Files:**
- Create: `src/ShortLink.Api/Infrastructure/Retry.cs`
- Test: `tests/ShortLink.Api.Tests/RetryTests.cs`

**Interfaces:**
- Produces: `public static Task Retry.RunAsync(Func<CancellationToken, Task> operation, int maxAttempts, Func<int, TimeSpan> delayFor, Func<TimeSpan, CancellationToken, Task> sleep, CancellationToken ct = default)`. Retries `operation` up to `maxAttempts` times; on each failure before the last attempt it awaits `sleep(delayFor(attempt), ct)`; the final failure's exception propagates. `delayFor` receives the 1-based attempt number.

- [ ] **Step 1: Write the failing test**

Create `tests/ShortLink.Api.Tests/RetryTests.cs`:

```csharp
extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class RetryTests
{
    private static Task NoSleep(TimeSpan _, CancellationToken __) => Task.CompletedTask;
    private static TimeSpan NoDelay(int _) => TimeSpan.Zero;

    [Fact]
    public async Task RunAsync_succeeds_on_first_attempt_calls_once()
    {
        var calls = 0;
        await Retry.RunAsync(_ => { calls++; return Task.CompletedTask; }, maxAttempts: 5, NoDelay, NoSleep);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunAsync_retries_then_succeeds()
    {
        var calls = 0;
        var sleeps = 0;
        await Retry.RunAsync(
            _ => { calls++; if (calls < 3) throw new InvalidOperationException("boom"); return Task.CompletedTask; },
            maxAttempts: 5,
            NoDelay,
            (_, __) => { sleeps++; return Task.CompletedTask; });
        Assert.Equal(3, calls);
        Assert.Equal(2, sleeps);
    }

    [Fact]
    public async Task RunAsync_exhausts_and_throws_last_exception()
    {
        var calls = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.RunAsync(
                _ => { calls++; throw new InvalidOperationException($"fail {calls}"); },
                maxAttempts: 3,
                NoDelay,
                NoSleep));
        Assert.Equal(3, calls);
        Assert.Equal("fail 3", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~RetryTests"`
Expected: FAIL — `Retry` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

Create `src/ShortLink.Api/Infrastructure/Retry.cs`:

```csharp
namespace ShortLink.Api.Infrastructure;

/// <summary>
/// Minimal async retry with caller-supplied backoff and sleep, so the delay policy
/// can be injected (real <see cref="Task.Delay(TimeSpan, CancellationToken)"/> in
/// production, a no-op in tests). Uses no reflection (AOT-safe).
/// </summary>
public static class Retry
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> operation,
        int maxAttempts,
        Func<int, TimeSpan> delayFor,
        Func<TimeSpan, CancellationToken, Task> sleep,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation(ct);
                return;
            }
            catch when (attempt < maxAttempts)
            {
                await sleep(delayFor(attempt), ct);
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ShortLink.Api.Tests --filter "FullyQualifiedName~RetryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ShortLink.Api/Infrastructure/Retry.cs tests/ShortLink.Api.Tests/RetryTests.cs
git commit -m "feat(api): inject-able async retry helper"
```

---

### Task 4: Wire Program.cs (CORS env, bootstrap retry, forwarded-headers comment)

**Files:**
- Modify: `src/ShortLink.Api/Program.cs:24-29` (CORS), `:33-36` (comment), `:50` (bootstrap)

**Interfaces:**
- Consumes: `CorsOrigins.Parse` (Task 2), `Retry.RunAsync` (Task 3), existing `Db.BootstrapAsync(NpgsqlDataSource, string, CancellationToken = default)`.
- Produces: startup that reads `CORS_ALLOWED_ORIGINS` and retries bootstrap; no new public API.

This task has no unit test of its own (it is composition in `Program.cs`); it is verified by the full test suite (which boots `Program` through `SliceTestHost`) plus the AOT publish gate.

- [ ] **Step 1: Replace the hardcoded CORS policy**

In `src/ShortLink.Api/Program.cs`, replace lines 24–29:

```csharp
// CORS for the Blazor WASM admin UI (hosted separately).
// AllowAnyHeader is required so X-Api-Key passes the CORS preflight.
// Allowed origins come from CORS_ALLOWED_ORIGINS (comma-separated); dev default is localhost.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(CorsOrigins.Parse(builder.Configuration["CORS_ALLOWED_ORIGINS"]))
    .AllowAnyHeader()
    .AllowAnyMethod()));
```

- [ ] **Step 2: Make the forwarded-headers comment host-neutral**

Replace the comment block at lines 33–36 (the `Trust X-Forwarded-For from the single immediate upstream (Fly proxy)...` comment) with:

```csharp
// Trust X-Forwarded-For from the single immediate upstream reverse proxy
// (Render's load balancer in production; Fly previously). KnownNetworks/KnownProxies
// are cleared from their loopback-only defaults so the platform proxy (a non-loopback
// peer) is accepted. ForwardLimit=1 trusts only the last hop, preventing XFF spoofing
// from multi-hop chains. ClientIp (used for rate limiting) resolves from this.
```

- [ ] **Step 3: Wrap bootstrap in retry**

Replace line 50 (`await Db.BootstrapAsync(dataSource, seedKey);`) with:

```csharp
// Retry bootstrap to absorb Neon cold-start on a Render wake: ~2+4+8+16+32s ≈ 62s
// across 6 attempts, comfortably under Render's 15-min deploy health window and above
// Neon's cold-start. Exhaustion rethrows — that indicates a real DB outage/misconfig.
await Retry.RunAsync(
    operation: token => Db.BootstrapAsync(dataSource, seedKey, token),
    maxAttempts: 6,
    delayFor: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
    sleep: (delay, token) => Task.Delay(delay, token));
```

- [ ] **Step 4: Build and run the full API test suite**

Ensure Postgres is up: `docker compose up -d`
Run: `dotnet test tests/ShortLink.Api.Tests -c Release`
Expected: PASS — all 122 existing tests plus the 8 new ones (Task 1–3) green. (Tests leave `CORS_ALLOWED_ORIGINS` unset, so the dev-default origin is used and startup/bootstrap succeed against the test DB.)

- [ ] **Step 5: Verify the AOT publish gate**

Run: `dotnet publish src/ShortLink.Api -c Release -r linux-x64`
Expected: succeeds with 0 warnings (confirms `CorsOrigins.Parse` and `Retry` introduce no IL2026/IL3050 trim/AOT warnings under `TreatWarningsAsErrors`).

- [ ] **Step 6: Commit**

```bash
git add src/ShortLink.Api/Program.cs
git commit -m "feat(api): env-driven CORS + resilient DB bootstrap for Render"
```

---

### Task 5: Cloudflare Pages static config + Web compression + ApiBaseUrl

**Files:**
- Create: `src/ShortLink.Web/wwwroot/_redirects`, `src/ShortLink.Web/wwwroot/_headers`
- Modify: `src/ShortLink.Web/ShortLink.Web.csproj`, `src/ShortLink.Web/wwwroot/appsettings.json`

**Interfaces:**
- Produces: publish output `out/wwwroot` containing `_redirects` + `_headers`, with no `*.br`/`*.gz` pre-compressed assets, and `appsettings.json` pointing at the Render API.

- [ ] **Step 1: Create `_redirects` (SPA fallback)**

Create `src/ShortLink.Web/wwwroot/_redirects`:

```
/*    /index.html   200
```

- [ ] **Step 2: Create `_headers` (cache policy)**

Create `src/ShortLink.Web/wwwroot/_headers`:

```
/_framework/*
  Cache-Control: public, max-age=31536000, immutable

/
  Cache-Control: no-store

/index.html
  Cache-Control: no-store
```

- [ ] **Step 3: Disable build-time compression in the Web csproj**

In `src/ShortLink.Web/ShortLink.Web.csproj`, add to the first `<PropertyGroup>`:

```xml
<!-- Cloudflare Pages compresses at the edge; skip Blazor's build-time .br/.gz assets. -->
<CompressionEnabled>false</CompressionEnabled>
```

- [ ] **Step 4: Point ApiBaseUrl at the Render API**

Replace the contents of `src/ShortLink.Web/wwwroot/appsettings.json` with:

```json
{
  "ApiBaseUrl": "https://slicefx-shortlink-api.onrender.com"
}
```

- [ ] **Step 5: Publish and verify the output**

Run:
```bash
dotnet workload install wasm-tools
dotnet publish src/ShortLink.Web -c Release -o out
ls out/wwwroot/_redirects out/wwwroot/_headers
find out/wwwroot \( -name '*.br' -o -name '*.gz' \) | head
```
Expected: `_redirects` and `_headers` exist in `out/wwwroot`; the `find` prints **nothing** (no pre-compressed assets). Note the `\( … \)` grouping — without it, `find`'s implicit `-print` binds only to the `-o` right operand and silently misses `*.br` files.
If `.br`/`.gz` still appear, replace `<CompressionEnabled>false</CompressionEnabled>` with `<BlazorEnableCompression>false</BlazorEnableCompression>` and re-run this step until the `find` is empty.

- [ ] **Step 6: Commit**

```bash
git add src/ShortLink.Web/wwwroot/_redirects src/ShortLink.Web/wwwroot/_headers src/ShortLink.Web/ShortLink.Web.csproj src/ShortLink.Web/wwwroot/appsettings.json
git commit -m "feat(web): Cloudflare Pages static config + point API at Render"
```

---

### Task 6: Rewrite deploy.yml for Render + Pages

**Files:**
- Modify (full rewrite): `.github/workflows/deploy.yml`

**Interfaces:**
- Consumes GitHub Secrets: `RENDER_DEPLOY_HOOK_URL`, `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` (created in Task 8). Uses built-in `GITHUB_TOKEN` for GHCR.
- Produces: on push to `main`, an updated Render service (from a sha-pinned GHCR image) and a fresh Cloudflare Pages deployment.

- [ ] **Step 1: Replace the workflow file**

Overwrite `.github/workflows/deploy.yml` with:

```yaml
name: Deploy

on:
  push:
    branches: [main]
  workflow_dispatch:

concurrency:
  group: deploy-${{ github.ref }}
  cancel-in-progress: false

jobs:
  deploy-api:
    name: Deploy API to Render
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4

      - uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: src/ShortLink.Api/Dockerfile
          platforms: linux/amd64
          push: true
          tags: |
            ghcr.io/sano-suguru/slicefx-shortlink-api:${{ github.sha }}
            ghcr.io/sano-suguru/slicefx-shortlink-api:latest
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Trigger Render deploy (sha-pinned)
        env:
          HOOK: ${{ secrets.RENDER_DEPLOY_HOOK_URL }}
          IMG: ghcr.io/sano-suguru/slicefx-shortlink-api:${{ github.sha }}
        run: |
          set +x
          # The hook URL already carries "?key=..."; append imgURL with "&".
          # Render requires the imgURL VALUE to be URL-encoded (it contains "/" and ":").
          # jq is preinstalled on ubuntu-latest; @uri percent-encodes the string.
          IMG_ENC=$(printf '%s' "$IMG" | jq -sRr @uri)
          curl -fsSL -X POST "${HOOK}&imgURL=${IMG_ENC}"

  deploy-web:
    name: Deploy Web to Cloudflare Pages
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Install wasm-tools workload
        run: dotnet workload install wasm-tools

      - name: Publish Blazor WASM
        run: dotnet publish src/ShortLink.Web -c Release -o out

      - name: Deploy to Cloudflare Pages
        uses: cloudflare/wrangler-action@v3
        with:
          apiToken: ${{ secrets.CLOUDFLARE_API_TOKEN }}
          accountId: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}
          wranglerVersion: "3.90.0"
          command: pages deploy out/wwwroot --project-name=slicefx-shortlink-web --branch=main
```

Note: the Pages project `slicefx-shortlink-web` is created once during console setup (Task 8), so `deploy-web` only deploys. Confirm `wranglerVersion` is the current latest v3 at implementation time and adjust the pin if needed.

- [ ] **Step 2: Validate the workflow YAML**

Run (if `actionlint` is installed): `actionlint .github/workflows/deploy.yml`
Otherwise run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/deploy.yml')); print('yaml ok')"`
Expected: no errors / `yaml ok`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "ci: deploy API to Render + Web to Cloudflare Pages on push to main"
```

---

### Task 7: Update deployment docs

**Files:**
- Modify: `README.md` (deploy section), `CLAUDE.md` (deploy section)

**Interfaces:** none (documentation).

- [ ] **Step 1: Find the current Fly references**

Run: `grep -rn "fly\.dev\|flyctl\|Fly\.io\|fly deploy\|nginx" README.md CLAUDE.md`
Note each location; these describe the old Fly deployment.

- [ ] **Step 2: Rewrite the README deploy section**

In `README.md`, replace the deployment description with the new topology. Use this text (adapt surrounding headings to match the file):

```markdown
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
```

- [ ] **Step 3: Update CLAUDE.md deploy notes**

In `CLAUDE.md`, replace any Fly/nginx deployment description with a one-paragraph summary matching the README (API=Render/GHCR, Web=Cloudflare Pages, DB=Neon, deploy on push to main) and remove references to `fly.toml`/`nginx.conf` as active deploy artifacts.

- [ ] **Step 4: Verify no stale Fly references remain in docs**

Run: `grep -rn "fly\.dev\|flyctl\|fly deploy" README.md CLAUDE.md`
Expected: no matches (or only historical mentions clearly marked as former).

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: describe Render + Cloudflare Pages deployment"
```

---

### Task 8: Console setup (manual — user) — gates the merge

**Files:** none (external consoles + GitHub Secrets).

This task produces the accounts, services, and secrets the workflow needs. It MUST complete before Task 9 (merge). No code.

- [ ] **Step 1: Make the GHCR image public (or plan a PAT)**

The first successful `deploy-api` run creates the GHCR package. Plan to set the package `slicefx-shortlink-api` visibility to **public** (GitHub → Packages → Package settings) so Render pulls without credentials. (Alternative: keep private and register a `read:packages` GitHub PAT as a Render registry credential in Step 2.)

- [ ] **Step 2: Create the Render Web Service**

In the Render dashboard: New → Web Service → **Deploy an existing image** → `ghcr.io/sano-suguru/slicefx-shortlink-api:latest`. Region **Singapore**. Instance type **Free**. (The service's default image URL must be exactly `ghcr.io/sano-suguru/slicefx-shortlink-api` — Render rejects a deploy-hook `imgURL` whose base path differs from the default; only the tag/digest may differ.) Set environment variables:
- `DATABASE_URL` = the Neon `postgres://…` URI (same as Fly used)
- `SeedApiKey` = the existing seed admin key (reuse the Fly value to keep the current key working)
- `BaseUrl` = the service's own public URL, e.g. `https://slicefx-shortlink-api.onrender.com` (set after the URL is known; must match exactly)
- `CORS_ALLOWED_ORIGINS` = `https://slicefx-shortlink-web.pages.dev` (the Pages URL from Step 3)
- `PORT` = `8080`

Set **Health Check Path** = `/health`. Copy the **Deploy Hook URL** (Settings → Deploy Hook).

- [ ] **Step 3: Create the Cloudflare Pages project + API token**

In Cloudflare dashboard: Workers & Pages → Create → Pages → **Direct Upload** → name it `slicefx-shortlink-web` (this reserves `slicefx-shortlink-web.pages.dev` and lets the workflow deploy to it). **Set the project's Production branch to `main`** (Settings → Builds & deployments) so the workflow's `--branch=main` deploys to production (`*.pages.dev`) rather than a preview URL. Create an API token with **Cloudflare Pages: Edit** permission; note the token and your **Account ID**.

- [ ] **Step 4: Register GitHub Secrets**

In the repo → Settings → Secrets and variables → Actions, add:
- `RENDER_DEPLOY_HOOK_URL` = the Render Deploy Hook URL from Step 2
- `CLOUDFLARE_API_TOKEN` = the token from Step 3
- `CLOUDFLARE_ACCOUNT_ID` = the account ID from Step 3

- [ ] **Step 5: Confirm readiness and reconcile hostnames**

Verify all three secrets exist and the Render service + Pages project are created. Then reconcile the actual assigned hostnames (Render appends a suffix if the service name was taken):
- `BaseUrl` and `CORS_ALLOWED_ORIGINS` (Render env) reflect the real Render/Pages URLs.
- **`src/ShortLink.Web/wwwroot/appsettings.json` `ApiBaseUrl` equals the real Render URL.** Task 5 hardcoded the proposed URL; if the assigned Render URL differs, update this file on `feat/migrate-render-pages` and commit before Task 9 (the Pages deploy in Task 9 ships whatever `appsettings.json` is on `main`).

---

### Task 9: Cutover (merge + verify)

**Files:** none (merge + live verification).

- [ ] **Step 1: Merge the branch to main**

Open a PR from `feat/migrate-render-pages`, ensure CI (`build.yml`) is green, and merge. The push to `main` triggers `deploy.yml` (both jobs). Note: `deploy.yml` runs on push to `main` independently of `build.yml` (no `needs`/gate between workflows); merging only after CI is green is a manual discipline. Optionally add a branch-protection rule requiring the CI check before merge to enforce it.

- [ ] **Step 2: Watch the deploy**

Run: `gh run watch $(gh run list --workflow=deploy.yml --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status`
Expected: both `deploy-api` and `deploy-web` succeed. (First `deploy-api` run also creates the GHCR package — set it public per Task 8 Step 1 if not already.)

- [ ] **Step 3: Verify the API**

Run:
```bash
curl -fsS https://slicefx-shortlink-api.onrender.com/health
curl -fsS https://slicefx-shortlink-api.onrender.com/health/ready
```
Expected: `/health` returns `{"status":"ok",...}` immediately; `/health/ready` returns 200 once the DB is reached (first call may take 30–60s while the free instance wakes).

- [ ] **Step 4: End-to-end check**

Open `https://slicefx-shortlink-web.pages.dev`, log in with the admin API key, create a link, follow the short URL (expect a 302 to the target), and confirm stats increment. This exercises CORS (`X-Api-Key` preflight), `BaseUrl` (302 `Location`), and DB reads/writes.

- [ ] **Step 5: Verify rate-limit client IP (ForwardedHeaders)**

Confirm per-IP rate limiting keys on the real client, not a shared proxy IP: make several rapid `POST` requests to the public-create endpoint from one client and confirm the limit triggers per that client. If all requests share one bucket (limit trips instantly for everyone) or never trip, revisit `ForwardLimit`/`KnownIPNetworks` (see spec §"ForwardedHeaders"). Note the outcome.

---

### Task 10: Remove Fly artifacts (post-cutover only)

**Files:**
- Delete: `fly.toml`, `src/ShortLink.Web/fly.toml`, `src/ShortLink.Web/Dockerfile`, `src/ShortLink.Web/nginx.conf`

**Do this only after Task 9 verification passes.** Git history preserves the files.

- [ ] **Step 1: Confirm nothing references them**

Run: `grep -rn "fly.toml\|nginx.conf\|ShortLink.Web/Dockerfile" --include="*.yml" --include="*.yaml" --include="*.csproj" --include="*.props" .`
Expected: no active references (the new `deploy.yml` uses none of them).

- [ ] **Step 2: Delete and commit**

```bash
git rm fly.toml src/ShortLink.Web/fly.toml src/ShortLink.Web/Dockerfile src/ShortLink.Web/nginx.conf
git commit -m "chore: remove Fly.io deploy artifacts after Render/Pages cutover"
```

- [ ] **Step 3: Delete the old Fly apps and rotate the DB credential**

Delete the two Fly apps (`slicefx-shortlink`, `slicefx-shortlink-web`) from the Fly dashboard. In Neon, rotate the database password and update `DATABASE_URL` in Render only — this invalidates the credential the retired Fly apps held (prevents dual-write to the same DB). Verify the API still serves after Render picks up the new `DATABASE_URL`.

---

## Self-Review

**Spec coverage:**
- CORS config-driven (AOT-safe) → Task 2 + 4. ✓
- Bootstrap retry + connection timeout → Task 1 + 3 + 4. ✓
- Render `/health` liveness, `PORT=8080` → Task 8 (console), verified Task 9. ✓
- ForwardedHeaders host-neutral + ClientIp verify → Task 4 (comment) + Task 9 Step 5. ✓
- Pages `_redirects`/`_headers`, pre-compression suppression, ApiBaseUrl → Task 5. ✓
- deploy.yml (2 jobs, packages:write, buildx gha cache, sha-pinned hook with `&imgURL`, wrangler pinned, log mask) → Task 6. ✓
- GitHub Secrets, GHCR public, Render/Pages/token setup → Task 8. ✓
- Cutover order (API up → appsettings already set in Task 5 → Pages deploy → CORS origin set in console before merge → E2E) → Task 8 + 9. ✓
- Fly teardown + Neon credential rotation → Task 10. ✓
- Docs → Task 7. ✓

**Placeholder scan:** No TBD/TODO. The one conditional (Task 5 Step 5 compression property fallback) is a concrete verify-then-fallback with exact commands and exact alternate property, not a vague instruction. Task 6 `wranglerVersion` and Task 8 hostnames are concrete values flagged for confirmation against live state — acceptable for an ops plan.

**Type consistency:** `CorsOrigins.Parse(string?) : string[]` used identically in Task 2 and Task 4. `Retry.RunAsync(operation, maxAttempts, delayFor, sleep, ct)` defined in Task 3 and called with named args in Task 4. `Db.BootstrapAsync(dataSource, seedKey, token)` matches the existing 3-arg signature. `CORS_ALLOWED_ORIGINS` env name consistent across Tasks 4, 6, 8 and the spec.
