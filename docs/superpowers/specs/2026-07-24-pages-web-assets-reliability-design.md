# Cloudflare Pages Web Asset Reliability Design

## Problem

The production Blazor WebAssembly site fails during startup because requests
for fingerprinted framework assets return `index.html` instead of the published
asset. For example,
`/_framework/System.Private.CoreLib.w8q95allq2.wasm` returns a 482-byte HTML
document whose SHA-256 is the digest reported by the browser's SRI failure.

The local `dotnet publish` output contains the requested file, and the deploy
workflow reports that all 67 static assets were accepted. Both the production
hostname and the immutable deployment hostname omit the same runtime files.
The failure is therefore between the successful publish output and the Pages
deployment manifest or asset serving layer.

The existing catch-all `_redirects` rule turns missing framework files into
successful HTML responses. The `/_framework/*` cache rule then marks those
responses immutable for one year, obscuring the actual missing-asset failure.

## Goals

- Reject incomplete Blazor publish output before deployment.
- Verify a Pages preview deployment byte-for-byte before production promotion.
- Verify the resulting production deployment as a final deployment gate.
- Return a real 404 for missing framework files instead of `index.html`.
- Keep direct navigation to the `/admin` Blazor route working.
- Update the pinned Wrangler CLI from `3.90.0` to the current `4.113.0`.

## Non-goals

- Migrate the site from Cloudflare Pages to Workers Static Assets.
- Change the API, database, CORS origin, or public Pages hostname.
- Add application features or modify the Blazor UI.
- Automatically roll back a production deployment through the Cloudflare API.

## Design

### Artifact verifier

Add `scripts/verify_web_assets.py`, implemented only with the Python standard
library.

Local verification accepts `--directory` and requires:

- `index.html`
- `_framework/blazor.webassembly.js`
- `_framework/dotnet.js`
- one `System.Private.CoreLib.*.wasm`
- one `dotnet.runtime.*.js`
- one `dotnet.native.*.wasm`

Every `.wasm` file must begin with the WebAssembly magic bytes `00 61 73 6d`.
JavaScript framework files must not begin with HTML.

Remote verification additionally accepts `--base-url`. It downloads every
file under the local `_framework` directory and compares its SHA-256 with the
local publish output. Requests retry ten times with a three-second interval so
normal Pages propagation does not create a false failure. A missing file,
fallback HTML, or byte mismatch fails the command.

### Two-stage Pages deployment

The workflow publishes once and verifies the local output. It then deploys the
same directory to a commit-specific preview branch, verifies the deployment URL
returned by `cloudflare/wrangler-action`, and only then deploys to `main`.
Finally, it verifies the production deployment URL.

Both Pages actions use `cloudflare/wrangler-action@v3` with Wrangler pinned to
`4.113.0`. The action major remains `v3` because that is the current official
action interface; `wranglerVersion` controls the CLI version.

This arrangement prevents an incomplete preview from replacing production.
The final check also exposes a production-only Pages manifest failure in the
GitHub Actions result.

### Routing

Add a top-level `404.html`. Cloudflare Pages then returns a real 404 for unknown
asset paths rather than applying its implicit SPA fallback.

Replace the catch-all redirect with the only non-root client route currently
used by the application:

```text
/admin    /    200
```

The root continues to resolve to `index.html`. Proxying `/admin` to `/` avoids
Cloudflare Pages canonicalizing `/index.html` into a 308 redirect that would
discard the client route. Direct `/admin` navigation therefore continues to
start Blazor at the original URL, and missing `/_framework` files no longer
masquerade as successful HTML responses.

## Testing

Unit tests cover:

- acceptance of a complete local Blazor artifact set;
- rejection of HTML stored under a `.wasm` filename;
- rejection of missing critical runtime files;
- successful remote byte comparison;
- rejection of a remote HTML fallback or digest mismatch.

Workflow configuration tests assert the pinned Wrangler version, preview-before-
production ordering, verification steps, and the absence of a catch-all SPA
rewrite.

The final verification runs:

1. Python unit tests for the verifier and workflow contract.
2. `dotnet publish src/ShortLink.Web -c Release -o out`.
3. Local verification against `out/wwwroot`.
4. Existing .NET tests with the repository's PostgreSQL test service when
   Docker is available.

## Operational behavior

If the Pages preview is incomplete, the workflow fails before production
deployment and prints the exact missing or mismatched URL. If production alone
is incomplete, the workflow fails after deployment and identifies the affected
asset; the operator can use the last known-good Pages deployment for rollback.
