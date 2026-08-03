# syntax=docker/dockerfile:1.7

FROM node:22-bookworm-slim AS frontend-build
WORKDIR /src/react

COPY react/package.json react/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

COPY react/ ./
ARG VITE_SESSION_INACTIVITY_MINUTES=45
ENV VITE_SESSION_INACTIVITY_MINUTES=${VITE_SESSION_INACTIVITY_MINUTES}
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src

COPY src/BugTracker.Api/BugTracker.Api.csproj src/BugTracker.Api/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/BugTracker.Api/BugTracker.Api.csproj

COPY src/BugTracker.Api/ src/BugTracker.Api/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/BugTracker.Api/BugTracker.Api.csproj \
      --configuration Release \
      --no-restore \
      --output /out \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/wwwroot /data/audit \
    && chown -R root:root /app \
    && chmod 0555 /app /app/wwwroot \
    && chown -R app:app /data \
    && chmod 0750 /data /data/audit

WORKDIR /app
COPY --from=api-build --chown=root:root /out/ ./
COPY --from=frontend-build --chown=root:root /src/react/dist/ ./wwwroot/
RUN chmod -R a-w /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    Database__Path=/data/bug_tracker.db \
    Audit__LogDirectory=/data/audit

VOLUME ["/data"]
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live >/dev/null || exit 1

ENTRYPOINT ["dotnet", "BugTracker.Api.dll"]
