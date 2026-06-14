# Build from repository root:
#   docker build -f src/ShortLink.Api/Dockerfile -t shortlink-api .
#
# On Apple Silicon (arm64) force linux/amd64 for a linux-x64 binary:
#   docker build --platform linux/amd64 -f src/ShortLink.Api/Dockerfile -t shortlink-api .
#
# Run (M1: no Postgres needed):
#   docker run --rm -p 8080:8080 shortlink-api
#   curl http://localhost:8080/health

# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# NativeAOT on Linux requires clang and zlib1g-dev
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

RUN dotnet publish src/ShortLink.Api/ShortLink.Api.csproj \
    -c Release \
    -r linux-x64 \
    -o /app

# ── Runtime stage ─────────────────────────────────────────────────────────────
# runtime-deps:10.0-noble-chiseled: minimal Ubuntu 24.04 distroless, non-root, ~13 MB.
# Includes libssl (OpenSSL) — required for Postgres TLS (added in M2).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled

WORKDIR /app
COPY --from=build /app/ .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["./ShortLink.Api"]
