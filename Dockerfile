# syntax=docker/dockerfile:1
# Multi-stage build for Nook (.NET 10 Blazor Server, single project).
# Mirrors the Kenman Design Studio / AWBlazor image conventions on the
# alibalib platform: localhost-only container, nginx terminates TLS.

ARG DOTNET_VERSION=10.0

# ---- build ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore as a cache-friendly layer — copy only the .csproj first.
COPY Nook.csproj .
RUN dotnet restore Nook.csproj

# Copy the rest of the source and publish. The test project is excluded via
# .dockerignore, and Nook.csproj also removes Nook.Tests/** from its globs.
COPY . .
RUN dotnet publish Nook.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# ICU for MudBlazor culture formatting; tzdata for correct timestamps.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata libicu-dev \
 && rm -rf /var/lib/apt/lists/*

# Non-root user for the app process.
RUN groupadd -r nook && useradd -r -g nook -m -d /home/nook nook

COPY --from=build /app/publish .

# App_Data holds DataProtection keys (Blazor antiforgery) — keep it writable + persistent.
RUN mkdir -p /app/App_Data && chown -R nook:nook /app

USER nook

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nook.dll"]
