# 本番デプロイ移行設計: Fly.io → Render + Cloudflare Pages + Neon

- **日付**: 2026-07-10
- **対象**: `slicefx-shortlink`（URL 短縮サービス）の本番ホスティング移行
- **ステータス**: 設計確定（実装計画へ）

## 背景と目的

Fly.io の無料枠が新規顧客向けに終了し、既存デプロイが `trial has ended, please add a credit card` で失敗する状態になった。カード登録を避けて完全無料で運用継続できる構成へ移行する。

現行構成は Fly.io 上の 2 アプリ（API=`slicefx-shortlink`、Web=`slicefx-shortlink-web`、共に `nrt`、`min_machines_running=0` でスリープ許容）+ 外部 Neon Postgres。

## 移行後アーキテクチャ

| コンポーネント | 現行 (Fly) | 移行後 |
| --- | --- | --- |
| API (.NET 10 NativeAOT) | Fly App `slicefx-shortlink` (nrt) | **Render Web Service**（無料 / Singapore / GHCR プレビルドイメージ pull） |
| Web (Blazor WASM 静的) | Fly App + nginx (nrt) | **Cloudflare Pages**（無料 / 静的配信） |
| DB (Postgres) | Neon (ap-southeast-1) | **Neon 据え置き**（変更なし） |
| デプロイ | `workflow_dispatch` → flyctl | **push-to-main → GitHub Actions**（自動） |

```
                 push to main
                      │
             ┌────────┴─────────┐  GitHub Actions (.github/workflows/deploy.yml)
             ▼                  ▼
   [job: deploy-api]     [job: deploy-web]
   NativeAOT image        Blazor WASM publish
   → GHCR :{sha},:latest  → wrangler pages deploy
   → Render Deploy Hook          │
     (?imgURL=…:{sha})           ▼
             │            Cloudflare Pages (static)
             ▼            slicefx-shortlink-web.pages.dev
   Render Web Service            │  ApiBaseUrl (起動時 fetch する静的 JSON)
   slicefx-shortlink-api.onrender.com
             │
             ▼
        Neon Postgres (ap-southeast-1, 変更なし)
```

### 決定事項（確定済み）

- **ドメイン**: デフォルトホスト名を使用（API=`<service>.onrender.com` / Web=`<project>.pages.dev`）。独自ドメインは取得しない。
- **デプロイトリガー**: push-to-main で自動デプロイ。
- **API ビルド戦略**: Render 側でビルドせず、GitHub Actions で NativeAOT イメージをビルドして GHCR に push し、Render はプレビルドイメージを pull する。理由: (1) Render 無料ビルド機での NativeAOT リンクはメモリ負荷が高く OOM/遅延リスク、(2) 既存 CI の AOT ビルド実績を再利用できる。
- **リージョン**: Render は Singapore（Neon の ap-southeast-1 と同リージョンで DB レイテンシ最小）。
- **命名（提案）**: Render サービス名 `slicefx-shortlink-api`（→ `slicefx-shortlink-api.onrender.com`）、Pages プロジェクト名 `slicefx-shortlink-web`（→ `slicefx-shortlink-web.pages.dev`）。onrender.com のサービス名はグローバル一意のため、取得済みの場合は別名になり公開 URL も変わる。よって `BaseUrl`／`Cors__AllowedOrigins`／`ApiBaseUrl` は**サービス作成後に確定した実 URL** で設定する。

### 受容済みトレードオフ

- Render 無料は 15 分アイドルでスリープ → 初回アクセスで 30–60 秒のコールドスタート。短縮リンクの初回 302 が遅れる場合がある。Fly も `min_machines_running=0` で scale-to-zero だったため許容範囲。
- 常時ウォーム化（cron ping）は 750h/月をほぼ使い切る（730h ≈ 常時稼働）ため既定では行わない。

## ホスト名の相互参照と解決順

3 つの連動点があり、ホスト名が決定的（`<service>.onrender.com` / `<project>.pages.dev`）であることを利用して順序で解決する。

1. API の `BaseUrl` = 自身の Render 公開ホスト名（302 Location と短縮 URL の基点。**厳密一致必須**、不一致だと生成リンクが壊れる）。
2. API の CORS 許可オリジン = Web の Pages ホスト名。
3. Web の `ApiBaseUrl` = API の Render ホスト名。

## コード変更（API）

### 1. CORS を設定駆動化（AOT 安全な実装に限定）

現状 `Program.cs` にハードコード（`WithOrigins("http://localhost:5201", "https://slicefx-shortlink-web.fly.dev")`）。これを環境変数駆動にする。

**制約**: `PublishAot=true` かつ CI が warnings-as-errors + AOT publish gate。`IConfiguration.Get<string[]>()` はリフレクションベースのバインダを使い IL2026/IL3050 を出して AOT gate を落とす。したがって**ジェネリックバインダを使わない**。

- 実装: 単一 env `Cors__AllowedOrigins` をカンマ区切りで受け `.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)` で配列化。未設定時は dev 既定 `http://localhost:5201` にフォールバック。
- `.AllowAnyHeader()`（`X-Api-Key` プリフライト通過に必須）と `.AllowAnyMethod()` は維持。`AllowCredentials()` は使わない（認証は `X-Api-Key` ヘッダで Cookie 非依存）。
- 検証: 変更後に `dotnet publish src/ShortLink.Api -c Release -r linux-x64`（AOT gate 相当）が警告ゼロで通ること。

### 2. 起動時 DB bootstrap の堅牢化

`Program.cs:50` の `await Db.BootstrapAsync(dataSource, seedKey)` は `app.Run()` 前にブロッキング実行され、例外時はプロセスが即終了する。Render のコールドスタート時に Neon も同時にアイドル復帰するため、初回接続が遅延・失敗するとクラッシュループに陥る恐れがある。

- `BootstrapAsync` 呼び出しをリトライで包む（例: 最大 5 回、指数バックオフ、最終失敗時のみ throw）。
- `Db.NormalizeUri` の `NpgsqlConnectionStringBuilder` に `Timeout = 30`（接続タイムアウト、既定 15s を延長）を追加。必要に応じて `CommandTimeout` も。
- `schema.sql` は `CREATE TABLE IF NOT EXISTS`、seed は `ON CONFLICT DO NOTHING` で冪等。無料枠は単一インスタンスのため同時起動競合はほぼ発生しない。毎コールドスタートで再実行されるが冪等かつ低コスト。

### 3. Render のポート整合

Render は前段プロキシからサービスの listen ポートを検出する。Dockerfile は `ASPNETCORE_URLS=http://+:8080`（全インターフェース bind = Render 必須の `0.0.0.0` を満たす）で固定。Render 側 env に `PORT=8080` を設定して検出ポートを一致させる。

### 4. ForwardedHeaders のホスト中立化

`Program.cs` の `UseForwardedHeaders` 設定は「Fly プロキシ前提」でコメント・`ForwardLimit=1` が組まれている。Render も前段プロキシが `X-Forwarded-For` を付けるため `ForwardLimit=1`（直近ホップのみ信頼）は妥当。コメントを host 中立に更新し、**レート制限が依存する `ClientIp` の正しさを Render 実環境で検証**する（`RateLimitStore` は `ClientIp` キー）。

## Web 成果物（Cloudflare Pages）

nginx.conf の役割を Pages の設定ファイルへ置換する。

- `src/ShortLink.Web/wwwroot/_redirects`: `/*  /index.html  200`（明示 SPA fallback）。Pages は実在の静的アセットを優先配信するため `/_framework/*` は shadow されない。Pages は `404.html` 不在時に自動 SPA fallback もするが、意図明示のため置く。
- `src/ShortLink.Web/wwwroot/_headers`: `/_framework/*` を `Cache-Control: public, max-age=31536000, immutable`、`/index.html` を `no-store` 相当。
- **プリ圧縮抑止**: Blazor publish が生成する `.br`/`.gz` を Pages 配信から除外（または `CompressionEnabled=false` で生成抑止）。Pages が edge で自動圧縮するため二重・冗長を避ける。
- `.wasm` の MIME は Pages が `application/wasm` を自動付与（nginx の手動 `types` は不要）。
- `<base href="/" />`（`index.html`）はルート配信なので変更不要。
- `ApiBaseUrl`: `wwwroot/appsettings.json` はコンパイル時定数ではなく**ブラウザ起動時に fetch される静的 JSON**。API ホスト変更はこの JSON を差し替えて Pages を再デプロイすれば足りる（C# 再コンパイル不要／本パイプラインはどのみち再ビルド）。現状 `fly.dev` がハードコードされているため cutover で Render URL に更新する。

## デプロイパイプライン（`.github/workflows/deploy.yml`）

Fly 版 deploy.yml を置換。push-to-main トリガー、2 ジョブ並列独立。

### job: deploy-api

- `permissions: { contents: read, packages: write }`。
- buildx で `linux/amd64`、repo-root コンテキストで `src/ShortLink.Api/Dockerfile` をビルド。
- GHCR に `ghcr.io/sano-suguru/slicefx-shortlink-api:{sha}` と `:latest` を push。
- `curl -fsSL -X POST "$RENDER_DEPLOY_HOOK_URL?imgURL=ghcr.io/sano-suguru/slicefx-shortlink-api:{sha}"` で Render にその sha を pin pull させる（`:latest` mutable の push/hook レースとロールバック不能を回避）。

### job: deploy-web

- .NET 10 SDK + `wasm-tools` workload セットアップ。
- `dotnet publish src/ShortLink.Web -c Release -o out`。
- 出力 `wwwroot` に `_redirects` / `_headers` を同梱、プリ圧縮ファイルを除外。
- `wrangler pages project create slicefx-shortlink-web --production-branch main || true`（冪等）。
- `wrangler pages deploy out/wwwroot --project-name slicefx-shortlink-web`。
- 認証: `CLOUDFLARE_API_TOKEN`（"Cloudflare Pages: Edit" スコープ）+ `CLOUDFLARE_ACCOUNT_ID`。

### 部分失敗の扱い

2 ジョブ独立のため片方失敗時に API/Web のバージョン不整合が一時的に生じうる。`SliceApiClient.g.cs` は CI で同期担保され契約変更は稀なので通常は並列で可。**契約破壊を伴うリリース時のみ** `deploy-web` に `needs: deploy-api` を付けて API 先行にする。

## 必要な GitHub Secrets

| Secret | 用途 |
| --- | --- |
| `RENDER_DEPLOY_HOOK_URL` | Render の deploy hook（imgURL パラメータ付与で sha pin） |
| `CLOUDFLARE_API_TOKEN` | wrangler（Pages: Edit スコープ） |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare アカウント ID |

`GITHUB_TOKEN`（自動）で GHCR に push する（`packages: write` 権限）。

## コンソール作業（ユーザー実施）とリポジトリ成果物（こちら）の分担

**こちら（リポジトリ内）**: `deploy.yml` 書き換え、`Program.cs`（CORS/ForwardedHeaders コメント）、`Db.cs`（Timeout/リトライ）、`wwwroot/_redirects`・`_headers`、`appsettings.json` 更新、csproj のプリ圧縮設定、ドキュメント、カットオーバー手順書。

**ユーザー（コンソール/機密）**:
- Render: GHCR イメージから Web Service 作成（Singapore）、env 投入（`DATABASE_URL`／`SeedApiKey`／`BaseUrl`＝Render URL／`Cors__AllowedOrigins`＝Pages URL／`PORT=8080`）、health check path=`/health`、Deploy Hook URL 取得。
- GHCR イメージ可視性: **public を推奨**（イメージに秘密は含まれず全て runtime env、認証不要で最シンプル）。private とする場合は Render に `read:packages` 権限の GitHub PAT を registry credential として登録（抜けると pull 失敗で無言死）。
- Cloudflare: API トークン（Pages: Edit）発行、Account ID 確認。
- GitHub: 上表の Secrets 登録。

## カットオーバー手順

1. Render で API Web Service 作成、env 投入（`BaseUrl` は作成後に確定する Render 公開ホスト名で厳密一致させる）、health check=`/health`、初回 deploy。
2. `curl https://<api>.onrender.com/health` で疎通確認、`/health/ready` で DB 到達確認。
3. Web の `wwwroot/appsettings.json` の `ApiBaseUrl` を Render URL に更新。
4. Pages へ deploy。
5. Pages URL を API の `Cors__AllowedOrigins` に設定して Render 再デプロイ。
6. E2E 確認（管理 UI ログイン → リンク作成 → 302 リダイレクト → 統計）。
7. 旧 Fly 撤去: Fly アプリ 2 つを削除、`fly.toml`・`src/ShortLink.Web/fly.toml`・`src/ShortLink.Web/Dockerfile`・`nginx.conf`・旧 deploy.yml を整理。Fly に残していた Neon 資格情報の revoke 要否を確認（Fly 残置中は旧環境も DB 書き込み可能＝データ二重化リスク）。

### SeedApiKey の扱い

Neon は据え置きのため既存 `api_keys` テーブルの seed ハッシュは残る。移行時に**同じ `SeedApiKey` を投入すれば既存キーで継続**（`ON CONFLICT DO NOTHING`）。異なる値を使うと新キーが追加され旧キーと併存する（実害小）。

## ロールバック

- Render: 過去 deploy への UI ロールバック（`{sha}` pin により確実に特定バージョンへ戻せる）。
- Cloudflare Pages: デプロイ履歴から即時ロールバック。
- Neon: 不変。

## 実装時に一次検証する項目（断定しない）

- Neon コールドスタートの実測遅延（bootstrap リトライ/タイムアウト値の妥当性確認）。
- Render 無料枠のログ保持期間・リージョン制限の有無。
- `wrangler pages deploy` のプロジェクト事前作成要否（バージョン依存。`project create || true` で吸収）。
- Render 上での `ClientIp`／レート制限の正しさ（ForwardedHeaders のホップ構成）。

## スコープ外

- 独自ドメイン取得と DNS 設定。
- API の Cloudflare Workers/WASI 移植（Npgsql 直接依存のため大工事、今回対象外）。
- 常時ウォーム化（cron ping）。
