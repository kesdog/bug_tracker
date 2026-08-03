# Deployment

## Topology and constraints

Bug Tracker is packaged as one immutable image: ASP.NET Core serves the API, WebSocket endpoint, and the Vite assets copied into `wwwroot`. The process listens on container port `8080`; only `/data` is mutable and persistent. `/app` is root-owned and has no write bits, while Compose also makes the container root filesystem read-only. The image contains no database, WAL/SHM file, logs, or secrets.

Run **exactly one replica** while SQLite and the in-process WebSocket hub are in use. Multiple replicas can contend for the database and cannot share live socket state. Compose encodes this assumption and uses stop-first updates to avoid concurrent writers. A future horizontally scaled deployment requires an external database plus a shared notification/backplane design.

Separate frontend hosting is not preferred: a single origin avoids CORS drift, mixed-content failures, duplicate release coordination, and special routing for `/api` and WebSockets. It also makes the frontend and API an atomic, versioned artifact.

## Prerequisites and configuration

The application serves static files with SPA fallback and exposes unauthenticated `GET /health/live` and `GET /health/ready` endpoints. Liveness reports process health. Readiness verifies database access, the embedded migration set, writable database storage, at least `Readiness__MinimumFreeBytes` free space, and whether a reset is in progress. The default threshold is 100 MiB. The image-level health check uses liveness; Compose uses readiness so traffic is not sent before the database is usable.

Create runtime configuration and replace the secret placeholder:

```bash
cp .env.example .env
openssl rand -base64 48
# Put the generated value in Auth__TokenSecret in .env.
```

`.env` is injected at container start. `VITE_SESSION_INACTIVITY_MINUTES` is a compile-time value passed by Compose, so changing it requires rebuilding the image. The packaged frontend always uses same-origin `/api`; no API base URL is needed. `VITE_API_PROXY_TARGET` is optional and applies only to the local Vite development server. Set `Frontend__Origin` to the externally visible HTTPS origin and restrict `AllowedHosts` to the deployed hostname(s).

For public ingress, enable forwarded headers only after replacing the example address with the exact source IP of the TLS proxy:

```dotenv
AllowedHosts=demo.example.com
Frontend__Origin=https://demo.example.com
ForwardedHeaders__Enabled=true
ForwardedHeaders__ForwardLimit=1
ForwardedHeaders__KnownProxies__0=10.0.0.10
```

Do not use a wildcard or an unverified proxy address. The application rejects enabled forwarding without an explicit trusted proxy. Configure edge-level bot, aggregate request, and connection shedding separately from the application limits.

Compose publishes `${BIND_ADDRESS:-127.0.0.1}:${HOST_PORT:-8080}`. Keep the default loopback binding when a TLS proxy runs on the same host; this prevents clients from bypassing TLS and proxy controls by reaching the application port directly. Set `BIND_ADDRESS=0.0.0.0` only when a platform firewall, private ingress network, or equivalent control restricts direct access to the port. Never expose the plain-HTTP application port directly to an untrusted network.

Do not put real secrets in the Dockerfile, image build arguments, source control, or image registry. For a platform deployment, inject `Auth__TokenSecret` from that platform's secret manager rather than an env file. Rotate it deliberately because rotation invalidates issued bearer tokens.

## Persistent storage and ownership

Mount durable, backed-up storage at `/data`. The default database is `/data/bug_tracker.db` and audit files use `/data/audit`. The image runs as the non-root `app` user; `/app` remains root-owned and immutable. Compose drops every Linux capability, enables `no-new-privileges`, mounts the root filesystem read-only, and provides only a bounded `/tmp` tmpfs with `nodev`, `nosuid`, and `noexec`. A new named volume inherits the prepared `/data` ownership. For a bind mount, make the host directory writable by the image's numeric user:

```bash
# After image build, inspect the authoritative UID/GID instead of assuming it.
docker run --rm --entrypoint id bug-tracker:local
sudo chown -R <uid>:<gid> /srv/bug-tracker-data
```

Back up the SQLite database with an online SQLite backup procedure; do not copy a live database file independently of its WAL. Restore and migration procedures are documented in `migration.md`.

## Demo reset semantics

The demo template enables a daily reset at **04:00 UTC** with:

```dotenv
ASPNETCORE_ENVIRONMENT=Demo
DemoReset__Enabled=true
DemoReset__HourUtc=4
DemoReset__AllowedEnvironments__0=Demo
DemoReset__DrainTimeoutSeconds=30
AgentWebSocket__CloseTimeoutSeconds=5
AgentWebSocket__MaxConnections=100
AgentWebSocket__MaxConnectionsPerUser=5
```

On the first enabled startup, the absence of a previous reset causes an immediate seed rather than waiting until 04:00 UTC. Later startups run a catch-up reset when a scheduled reset was missed. During a live reset, the app enters maintenance, stops admitting API work, drains active API requests and outbox dispatch, and closes agent notification sockets. New API and socket requests temporarily receive `503 Service Unavailable` with `Retry-After: 60`; readiness also returns 503.

The data delete, deterministic seed, validation, and reset-state update run in one SQLite transaction. A successful commit exposes the complete new generation; a failure rolls back the reset. Reset deletes persisted authentication tokens, so existing browser/API sessions are invalidated, and connected sockets are closed. Clients must sign in again and reconnect after readiness recovers. WAL truncation and JSONL audit cleanup are persisted as post-commit cleanup steps; if either is temporarily blocked, startup and the scheduler retry cleanup without reseeding or incrementing the generation again. `DemoReset__DrainTimeoutSeconds` bounds the wait for active work before a reset fails safely; the default is 30 seconds. `AgentWebSocket__CloseTimeoutSeconds` bounds each socket close operation at 5 seconds, `AgentWebSocket__MaxConnections` caps the public demo at 100 sockets, and `AgentWebSocket__MaxConnectionsPerUser` limits one agent identity to 5 concurrent sockets. Keep these limits finite; tune them only from observed traffic and alerting.

This workflow requires **exactly one replica**. The application rejects reset in `Production` and requires the current environment to appear in `DemoReset__AllowedEnvironments`, but the operator must still never enable it for production data. The persistent volume survives container replacement and `docker compose down`; `docker compose down --volumes` intentionally destroys it.

## Deploy

```bash
# Validate the resolved model without exposing its output in shared logs.
docker compose --env-file .env config --quiet

# Build and start one replica.
docker compose --env-file .env build --pull
docker compose --env-file .env up -d --wait
docker compose ps
```

For a registry deployment, build and publish an immutable tag, set `BUG_TRACKER_IMAGE` to that tag, and use the same environment contract and `/data` mount. Do not scale beyond one replica.

## TLS edge and WebSockets

Terminate TLS at a provider-neutral reverse proxy or load balancer and forward to HTTP port `8080`. Redirect HTTP to HTTPS and emit HSTS at that edge after HTTPS is verified. Preserve the original host/scheme through standard forwarded headers, and allow WebSocket upgrade/long-lived connections for `/api/agent/notifications/ws`. Configure edge idle timeouts above the application's heartbeat and retry window. The app trusts forwarded headers only from configured known proxies; direct clients cannot select their own rate-limit partition with spoofed forwarding headers.

The Demo environment applies per-client authenticated request/write/export/upload/WebSocket limits and reset-generation resource budgets. Defaults cap the fixture plus visitor data at 25 projects, 300 tickets, 600 comments, 100 attachments, and 256 MiB of persisted evidence while preserving a 256 MiB filesystem reserve. Use a volume quota larger than the reserve and observe quota rejections before increasing these values.

## Future transactional email delivery

The public demo does not send email. When a non-demo deployment needs invitation delivery, keep request approval and account provisioning separate from the provider behind an `IAccountInvitationNotifier` boundary. Use a no-op implementation for Demo/local environments and a provider implementation, such as Brevo, only in the deployment that supplies its API key and fixed sender identity through a secret manager.

Queue production email through a durable outbox after the setup-token hash is committed. Do not put raw setup tokens or setup URLs in ordinary logs or plaintext outbox payloads; use protected delivery payloads or a purpose-built one-time delivery record. Record only a sanitized correlation ID, delivery state, retry count, and safe error detail. This keeps future provider swaps independent from the auth endpoint and avoids coupling a successful account request to a live email API.

## Smoke checks and rollback

```bash
ORIGIN=http://${BIND_ADDRESS:-127.0.0.1}:${HOST_PORT:-8080}
curl --fail --silent --show-error "$ORIGIN/health/live"
curl --fail --silent --show-error "$ORIGIN/health/ready"
curl --fail --silent --show-error "$ORIGIN/" >/dev/null
curl --fail --silent --show-error "$ORIGIN/api/auth/login" \
  -H 'Content-Type: application/json' \
  --data '{"email":"invalid@example.invalid","password":"invalid"}' || true
docker compose logs --since=5m app
```

Also perform an authenticated login and API read, direct-load an SPA route, and establish a WebSocket connection through the TLS edge. Monitor container health, restart count, HTTP 5xx/latency, SQLite busy errors, disk capacity, outbox age, and active WebSockets. Alert on sustained readiness failure or low disk space.

Rollback by redeploying the previous immutable image with unchanged configuration. Schema changes must remain backward-compatible for image-only rollback; otherwise follow the tested database restore procedure. Compose's rollback declaration is useful to compatible orchestrators, but plain `docker compose` does not automatically roll back a failed update.
