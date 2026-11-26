# syntax=docker/dockerfile:1@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
# (docker/dockerfile:1 resolved 2026-11-26, multi-arch index digest. Pinned like the base images: the frontend runs the whole build.)
# Tether API (src/api/src/Tether.Host). Build context: src/api
#   docker build -t tether-api:local src/api
# No secrets, connection strings or environment choice are baked in: everything comes from environment variables at run time.

# mcr.microsoft.com/dotnet/sdk:10.0 resolved 2026-11-26 to SDK 10.0.404 (Ubuntu 24.04 noble), multi-arch index digest.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
# Only the host and the projects it references are built; tests stay out of the image.
RUN --mount=type=cache,id=tether-nuget,target=/root/.nuget/packages \
    dotnet publish src/Tether.Host/Tether.Host.csproj -c Release -o /app/publish -p:UseAppHost=false

# mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra resolved 2026-11-26 to ASP.NET Core 10.0.15, multi-arch index digest.
# "extra" = chiseled (no shell, no package manager, non-root "app" user 1654) PLUS ICU and tzdata. Both are required:
# B1-10 (NFC normalisation needs ICU; InvariantGlobalization=false) and B1-14 (time zone rules); the Syllabus module refuses to start without them.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:c8dd08a50d8ffd19bd8da72edee05a7ac15b486a5b1869cf46fdb370bf1fbc76 AS runtime
WORKDIR /app
# The published FILES are root-owned and not writable by the app user. The /app DIRECTORY itself comes from the base image
# owned by uid 1654, so the process can still add or replace files in it. With Security:RootSecret unset in Development the
# API does exactly that: it writes its generated secret to /app/tether/dev-root-secret (the fallback path resolves relative
# to the working directory here). Always set Security__RootSecret (compose and Azure both do).
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
# Numeric uid so a platform can verify non-root without reading /etc/passwd.
USER 1654
ENTRYPOINT ["dotnet", "Tether.Host.dll"]
