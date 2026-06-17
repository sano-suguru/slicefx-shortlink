# Stage 1: build Blazor WASM
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Install wasm-tools workload required for BlazorWebAssembly publish
RUN dotnet workload install wasm-tools

COPY . .
RUN dotnet publish src/ShortLink.Web/ShortLink.Web.csproj -c Release -o /out

# Stage 2: serve static files with nginx
FROM nginx:alpine AS final
COPY --from=build /out/wwwroot /usr/share/nginx/html
COPY src/ShortLink.Web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
