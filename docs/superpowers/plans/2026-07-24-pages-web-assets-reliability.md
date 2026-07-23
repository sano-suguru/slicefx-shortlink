# Cloudflare Pages Web Asset Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent incomplete Blazor framework assets from reaching the production Cloudflare Pages hostname and make missing framework requests fail as 404 responses.

**Architecture:** A standard-library Python verifier validates the local publish directory and compares every deployed `_framework` asset byte-for-byte. GitHub Actions deploys and verifies a commit-specific preview before deploying production. Explicit `/admin` routing plus a top-level `404.html` replaces the catch-all SPA rewrite.

**Tech Stack:** .NET 10 Blazor WebAssembly, Python 3 standard library, GitHub Actions, `cloudflare/wrangler-action@v3`, Wrangler 4.113.0, Cloudflare Pages.

## Global Constraints

- Keep the existing public hostname `https://slicefx-shortlink-web.pages.dev`.
- Do not change API, database, Render, Neon, or CORS configuration.
- Use only Python's standard library for deployment verification.
- Compare deployed framework bytes with the exact output from the same publish job.
- A preview must pass verification before production deployment starts.
- Preserve direct navigation to `/admin`.

---

### Task 1: Local Blazor Artifact Verification

**Files:**
- Create: `tests/deploy/test_verify_web_assets.py`
- Create: `scripts/verify_web_assets.py`

**Interfaces:**
- Consumes: a Blazor publish directory supplied with `--directory`.
- Produces: process exit code `0` for a valid artifact set and `1` with one diagnostic per invalid asset.

- [ ] **Step 1: Write failing local-verification tests**

Create test helpers that build a temporary artifact tree containing
`index.html`, `blazor.webassembly.js`, `dotnet.js`,
`dotnet.runtime.test.js`, `dotnet.native.test.wasm`, and
`System.Private.CoreLib.test.wasm`. Invoke the script as a subprocess.

Cover these behaviors:

```python
def test_accepts_complete_local_publish(self): ...
def test_rejects_html_returned_as_wasm(self): ...
def test_rejects_missing_runtime_asset(self): ...
```

The valid WASM test bytes must begin with `b"\x00asm"`.

- [ ] **Step 2: Run tests to verify RED**

Run:

```bash
python3 -m unittest discover -s tests/deploy -p 'test_verify_web_assets.py' -v
```

Expected: FAIL because `scripts/verify_web_assets.py` does not exist.

- [ ] **Step 3: Implement minimal local verifier**

Implement:

```python
def collect_framework_files(directory: Path) -> list[Path]: ...
def verify_local(directory: Path) -> list[str]: ...
def main() -> int: ...
```

`verify_local` must check the six critical file patterns from the design, all
WASM magic bytes, and reject framework JavaScript whose leading bytes contain
`<!doctype html` or `<html`.

- [ ] **Step 4: Run tests to verify GREEN**

Run the command from Step 2.

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/verify_web_assets.py tests/deploy/test_verify_web_assets.py
git commit -m "test: validate Blazor publish assets"
```

### Task 2: Remote Byte-for-byte Deployment Verification

**Files:**
- Modify: `tests/deploy/test_verify_web_assets.py`
- Modify: `scripts/verify_web_assets.py`

**Interfaces:**
- Consumes: `--directory PATH`, `--base-url URL`, `--attempts N`, and `--delay SECONDS`.
- Produces: exit code `0` only when every local `_framework` file is returned with the same SHA-256 from the deployment URL.

- [ ] **Step 1: Write failing remote-verification tests**

Serve a second temporary artifact directory with
`ThreadingHTTPServer` and test real HTTP behavior:

```python
def test_accepts_remote_assets_with_matching_bytes(self): ...
def test_rejects_remote_html_fallback_for_wasm(self): ...
def test_rejects_remote_digest_mismatch(self): ...
```

Use `--attempts 1 --delay 0` so failure tests remain fast.

- [ ] **Step 2: Run tests to verify RED**

Run:

```bash
python3 -m unittest discover -s tests/deploy -p 'test_verify_web_assets.py' -v
```

Expected: the three new tests FAIL because `--base-url` is unsupported.

- [ ] **Step 3: Implement remote verification**

Implement:

```python
def sha256_bytes(data: bytes) -> str: ...
def verify_remote_once(directory: Path, base_url: str) -> list[str]: ...
def verify_remote(directory: Path, base_url: str, attempts: int, delay: float) -> list[str]: ...
```

Build each URL from the path relative to `directory`, download without
compression using `urllib.request`, reject non-2xx responses, reject HTML for
WASM or JavaScript, and compare SHA-256. Retry the complete check until it
passes or attempts are exhausted.

- [ ] **Step 4: Run tests to verify GREEN**

Run the command from Step 2.

Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/verify_web_assets.py tests/deploy/test_verify_web_assets.py
git commit -m "feat: verify deployed Pages assets byte for byte"
```

### Task 3: Safe Pages Routing and Two-stage Deployment

**Files:**
- Create: `tests/deploy/test_pages_deploy_config.py`
- Create: `src/ShortLink.Web/wwwroot/404.html`
- Modify: `src/ShortLink.Web/wwwroot/_redirects`
- Modify: `.github/workflows/deploy.yml`

**Interfaces:**
- Consumes: `scripts/verify_web_assets.py` from Tasks 1 and 2 and the official action outputs `deployment-url`.
- Produces: verified preview deployment followed by verified production deployment.

- [ ] **Step 1: Write failing workflow and routing contract tests**

Tests must assert:

```python
def test_framework_requests_are_not_caught_by_spa_rewrite(self): ...
def test_admin_route_is_rewritten_to_index(self): ...
def test_top_level_404_exists(self): ...
def test_workflow_pins_current_wrangler(self): ...
def test_workflow_verifies_preview_before_production(self): ...
def test_workflow_verifies_production(self): ...
```

The ordering test compares string offsets for local verification, preview
deployment, preview verification, production deployment, and production
verification.

- [ ] **Step 2: Run tests to verify RED**

Run:

```bash
python3 -m unittest discover -s tests/deploy -v
```

Expected: FAIL on the catch-all redirect, missing 404, Wrangler 3.90.0, and
missing verification stages.

- [ ] **Step 3: Implement routing changes**

Set `_redirects` to:

```text
/admin    /index.html    200
```

Add a minimal static `404.html` with a UTF-8 declaration, `noindex`, a concise
not-found message, and a link to `/`.

- [ ] **Step 4: Implement workflow changes**

After publish, run:

```yaml
- name: Verify published Web assets
  run: python3 scripts/verify_web_assets.py --directory out/wwwroot
```

Deploy a preview with an `id`, Wrangler `4.113.0`, and branch
`asset-smoke-${{ github.sha }}`. Verify
`${{ steps.preview_deploy.outputs.deployment-url }}` with ten attempts and a
three-second delay. Only then deploy `--branch=main` using a second action step
with `id: production_deploy`, followed by the same remote verification.

- [ ] **Step 5: Run tests to verify GREEN**

Run:

```bash
python3 -m unittest discover -s tests/deploy -v
```

Expected: all deployment tests pass.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/deploy.yml scripts/verify_web_assets.py \
  src/ShortLink.Web/wwwroot/_redirects src/ShortLink.Web/wwwroot/404.html \
  tests/deploy
git commit -m "fix: gate Pages deploys on complete Blazor assets"
```

### Task 4: Publish and Regression Verification

**Files:**
- Modify only if verification reveals a defect in files changed by Tasks 1-3.

**Interfaces:**
- Consumes: completed implementation.
- Produces: evidence that the publish output and repository checks pass.

- [ ] **Step 1: Run Python deployment tests**

```bash
python3 -m unittest discover -s tests/deploy -v
```

Expected: all tests pass.

- [ ] **Step 2: Publish the Web project**

```bash
dotnet publish src/ShortLink.Web -c Release -o out -m:1 -nr:false
```

Expected: publish succeeds with no errors.

- [ ] **Step 3: Verify the real publish output**

```bash
python3 scripts/verify_web_assets.py --directory out/wwwroot
```

Expected: reports the verified framework file count and exits `0`.

- [ ] **Step 4: Start the repository test database**

```bash
docker compose up -d postgres
```

Expected: the `postgres` service becomes healthy.

- [ ] **Step 5: Run the .NET regression suite**

```bash
ConnectionStrings__Postgres='Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=shortlink;SSL Mode=Prefer' \
SeedApiKey='ci-seed-key-for-tests' BaseUrl='http://localhost' \
dotnet test ShortLink.slnx -c Release -m:1 -nr:false
```

Expected: 130 tests pass.

- [ ] **Step 6: Check formatting and repository diff**

```bash
dotnet format ShortLink.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591
git diff --check
git status --short
```

Expected: no formatting or whitespace failures; status contains only intended
changes if any remain uncommitted.

- [ ] **Step 7: Commit verification-driven corrections if required**

Only if Steps 1-6 required a correction:

```bash
git add .github/workflows/deploy.yml scripts/verify_web_assets.py \
  src/ShortLink.Web/wwwroot/_redirects src/ShortLink.Web/wwwroot/404.html \
  tests/deploy
git commit -m "fix: correct Pages asset verification"
```
