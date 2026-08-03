# Bug Tracker

Bug Tracker is a React 19 and ASP.NET Core 10 application backed by SQLite. The deployment artifact serves the compiled SPA, HTTP API, and agent notification WebSocket from one non-root container on port `8080`.

## Local development

```bash
dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj
cd react && npm install && npm run dev
```

See `AGENTS.md` for build and test commands, `AUTH.md` for authentication behavior, `migration.md` for database operations, and `DEPLOYMENT.md` for the one-image deployment runbook.

## Container deployment

The provider-neutral image runs the API, notification WebSocket, and same-origin SPA as a non-root user with a read-only root filesystem and persistent state under `/data`. Compose binds to `127.0.0.1` by default so a same-host TLS proxy cannot be bypassed. Copy `.env.example` to the ignored `.env`, replace `Auth__TokenSecret`, and follow `DEPLOYMENT.md` to build and run the single-replica demo deployment. The demo is seeded immediately on first startup and resets every day at 04:00 UTC.
