# Do Before Release

This is the remaining release plan for the public portfolio demo. The deployment target is one Docker image, one writable replica, resettable synthetic data, SQLite under `/data`, and TLS termination at a trusted reverse proxy.

Completed work from the previous review has been removed. This document contains only unresolved findings, release work, and acceptance gates.

## Current Verdict

**CONDITIONAL GO for the constrained public portfolio demo.**

The five application blockers selected for this release were resolved on 2026-08-01. Internet exposure still requires the deployment operator to set the exact `AllowedHosts`, HTTPS `Frontend__Origin`, and trusted proxy IP, keep port 8080 private, and configure edge TLS/HSTS and load shedding as documented in `DEPLOYMENT.md`.

The application foundation is materially complete. Authorization, checksummed migrations, foreign-key enforcement, transactional ticket writes and outbox delivery, optimistic concurrency, report/image validation, report reachability, timeline semantics, exact dashboard summaries, cursor pagination, same-origin SPA/API/WebSocket delivery, health checks, and the atomic daily demo reset are implemented and covered by the existing automated suites.

Resolved public-demo blockers:

1. Authenticated abuse is bounded by Demo-only IP/user rate limits, reset-generation quotas, storage reserve admission, bounded bearer/audit/outbox retention, and structured `429`/`413` responses.
2. Shared fixture accounts bypass only the low identity bucket while retaining strict real-IP, aggregate-user, and global login limits.
3. CSV exports neutralize spreadsheet formulas before normal CSV quoting.
4. Application security headers and trusted-forwarder configuration are implemented; exact edge values remain deployment inputs.
5. The Demo login screen exposes role-based credentials, public/disposable-data warnings, and a short walkthrough without revealing fixtures outside Demo.

Deferred items 6 and 7 remain quality/reliability work, but are not blockers for the currently approved constrained public-demo scope.

## Verification Baseline

Verified on 2026-08-01:

- Frontend tests: **102 passed**.
- Backend tests: **166 passed**.
- Frontend production build: passed.
- Backend Release build: passed with zero warnings.
- `docker compose config --quiet`: passed.
- Docker image `bug-tracker:review`: built successfully.
- Fresh Docker volume reached `/health/ready` with migration version 8.
- Container health check reported healthy.
- Admin login through the built image succeeded.
- Direct `/setup-password` loading returned the SPA.
- An unknown `/api/*` route did not fall through to the SPA.
- Live browser inspection confirmed that demo credentials are absent from the login UI, authenticated pages duplicate their headings, and `favicon.ico` returns `404`.
- Frontend build warning: initial JavaScript bundle is approximately 1.21 MB minified and 367 KB gzip.

These checks do not replace the missing automated Docker/browser release suite.

## P1 Release Blockers

### 1. Bound Authenticated Abuse and Persistent-Volume Growth

**Resolved 2026-08-01.**

Routine authenticated requests no longer create durable `token_used` audit/outbox events. Presence writes are suppressed in memory and conditionally debounced in SQLite:

- `src/BugTracker.Api/Auth/AuthMiddleware.cs:53-79`

The Demo environment now applies independent trusted-client-IP and aggregate-user limits to general requests, writes, creation, exports, uploads, and WebSocket handshakes. Shared sessions cannot bypass the IP bucket by logging in again.

One-replica write admission serializes hard project, ticket, comment, attachment, and evidence-byte quota checks. A configurable filesystem reserve rejects storage-growing requests before reset/checkpoint capacity is consumed. Bearer records, audit rows, and processed outbox rows now have bounded retention.

#### Required Work

1. Remove routine `token_used` auditing from normal authenticated requests.
2. Debounce `last_seen_at` updates to at most once per user every 1-5 minutes.
3. Add public-demo rate-limit policies partitioned by trusted client IP and user/session for:
   - General authenticated requests.
   - Ticket and project creation.
   - Comments and lifecycle writes.
   - Image decoding and uploads.
   - Exports.
   - WebSocket handshakes. Existing concurrent-socket caps remain in force.
4. Add hard per-reset-generation quotas for projects, tickets, comments, attachments, and total attachment bytes.
5. Refuse storage-growing writes once the configured safety reserve is reached.
6. Preserve enough free space for a reset transaction, WAL checkpoint, and cleanup.
7. Delete or archive processed outbox rows on a bounded schedule instead of retaining them until reset only.
8. Stop persisting unauthenticated WebSocket failures as user-attributed audit rows based on caller-supplied identity hints.
9. Return structured `429` responses with `Retry-After`; use `413` for hard body/storage ceilings where appropriate.

#### Acceptance Criteria

- Repeated authenticated GET requests do not create one audit/outbox record per request.
- Presence writes are measurably debounced.
- Public accounts cannot exceed documented request, write, upload, or storage budgets.
- Repeated maximum-size uploads cannot fill `/data` or prevent the next reset.
- The safety reserve remains available after quota exhaustion.
- Rate and quota failures are structured, recoverable, and covered by integration tests.

### 2. Make Shared Demo Login Limits Lockout-Safe

**Resolved 2026-08-01.**

Public credentials remain intentionally shared. In Demo only, known fixture emails bypass the low shared identity bucket while retaining the real-client-IP limiter, a global login limiter, and authenticated aggregate-user limits:

- `src/BugTracker.Api/Auth/PublicEndpointRateLimiting.cs:13-18`
- `demo.md:5-13`

Forwarded headers remain disabled by default and require an explicit trusted proxy IP and exact forward limit. Deployment examples are in `.env.example`, `compose.yaml`, and `DEPLOYMENT.md`.

#### Required Work

1. Add a Demo-specific login policy that retains real-client-IP and global load limits without allowing one visitor to exhaust a low shared identity bucket.
2. Increase or remove the per-identity limit for known shared fixture accounts while retaining strict per-real-IP limits.
3. Configure forwarded headers for the actual TLS proxy with:
   - `ForwardedHeaders__Enabled=true`.
   - An explicit `KnownProxies` or trusted-network list.
   - `ForwardLimit=1` unless the selected topology requires another exact value.
4. Add a deployment test proving two external client IPs receive separate limiter partitions.
5. Document edge-level bot, connection, and global load-shedding limits.

#### Acceptance Criteria

- One remote visitor cannot lock every shared account for other IPs.
- The application observes the real client IP only through the configured trusted proxy.
- Arbitrary clients cannot spoof forwarded headers.
- Aggregate abusive login traffic remains bounded.

### 3. Neutralize CSV Spreadsheet Formulas

**Resolved 2026-08-01.**

Visitor-controlled ticket and project values are neutralized before normal CSV punctuation escaping:

- `src/BugTracker.Api/Bugs/BugEndpoints.Formatters.cs:97-138`

Dangerous leading formula characters, including those after leading whitespace/control characters, receive a literal-text prefix.

#### Required Work

1. Neutralize dangerous leading characters before CSV quoting, commonly by prefixing a single quote.
2. Account for leading whitespace and control characters.
3. Add tests for malicious issue titles, project names, tags, and identifiers.
4. Verify output in Excel-compatible and LibreOffice-compatible behavior.

#### Acceptance Criteria

- Exported malicious values display as inert text.
- Standard commas, quotes, and line breaks remain valid CSV.

### 4. Add Browser Security Headers and Final Edge Configuration

**Resolved in application code 2026-08-01. Exact hostname, HTTPS origin, and trusted proxy IP are deployment inputs.**

The application emits a nonce-based CSP plus frame, MIME-sniffing, referrer, and permissions restrictions for SPA, static, API, and error responses. MUI Emotion styles and the color-scheme initializer receive the per-response nonce.

#### Required Work

1. Add and integration-test application defaults for:
   - `Content-Security-Policy` with at least `frame-ancestors 'none'`, `object-src 'none'`, and `base-uri 'self'`.
   - `X-Content-Type-Options: nosniff`.
   - `Referrer-Policy: no-referrer` or `strict-origin-when-cross-origin`.
   - A restrictive `Permissions-Policy`.
   - Optional `X-Frame-Options: DENY` for legacy defense.
2. Configure HSTS and HTTP-to-HTTPS redirection at the TLS edge.
3. Restrict `AllowedHosts` to the deployed hostname.
4. Set `Frontend__Origin` to the exact external HTTPS origin.
5. Keep container port `8080` private; only the TLS proxy may reach it.
6. Validate WebSocket upgrades and idle timeouts through the edge.
7. Tune CSP against the built image without broad `unsafe-inline` or `unsafe-eval` allowances.

#### Acceptance Criteria

- Static, SPA, API, auth, and attachment responses carry the intended headers.
- The SPA and WebSocket function under the final CSP.
- The demo cannot be framed by another origin.
- Direct plaintext access to the container is not publicly reachable.

### 5. Make the Hosted Demo Self-Contained

**Resolved 2026-08-01.**

The built login page receives Demo account metadata at runtime only in the `Demo` environment:

- `react/src/components/AuthViews.jsx:49-56`

It includes role autofill, synthetic/public/mutable/reset warnings, visible-password context, and the five-minute scenario. Non-Demo responses contain no fixture metadata.

#### Required Work

1. In the `Demo` environment, show a concise public-demo banner on the login screen.
2. Explain that all data is synthetic, public, mutable by other visitors, and reset daily at 04:00 UTC.
3. Warn visitors not to enter personal or confidential data.
4. Provide role-based demo account selection or one-click autofill for dev, senior, and admin scenarios.
5. Keep passwords visible only because these are intentionally public synthetic accounts; clearly label that exception.
6. Link to a short five-minute guided scenario available from the deployed UI or README.
7. Ensure the demo panel does not render outside the Demo environment.

#### Acceptance Criteria

- A first-time visitor can enter the demo without repository access or external instructions.
- Dev, senior, and admin roles are understandable before login.
- The warning and account selector fit at `360x800` without horizontal overflow.
- Non-demo environments never reveal fixture credentials.

### 6. Add CI and Exact-Image Release Automation

There is no workflow under `.github/workflows`, and `react/package.json` has no Playwright, axe, lint, or E2E script. Existing hosting tests use TestServer rather than the built container.

#### Required Work

1. Add CI for:
   - Backend Release build and full tests.
   - Frontend production build and full tests.
   - Frontend linting/format checks.
   - Docker image build from a clean context.
   - Fresh-volume startup and readiness.
   - Migration, seed, login, SPA routing, API routing, and WebSocket smoke tests.
   - Playwright E2E against the built image.
   - axe accessibility checks.
   - Secret scanning.
   - NuGet, npm, and container dependency scanning.
   - SBOM generation and retention.
2. Fail release on unaccepted Critical or High dependency/container advisories.
3. Record the final image digest and SBOM as release artifacts.
4. Require release gates before deployment.

#### Acceptance Criteria

- A clean CI runner can build and verify the image without local artifacts.
- The container starts with a new volume and reaches readiness.
- CI validates that the image contains no database, WAL/SHM, logs, `.env`, test output, `node_modules`, or unintended source maps.
- A failed browser, accessibility, migration, security, or container smoke test blocks release.

### 7. Bound Reset and Container Shutdown

Reset waits for active request leases and aborts after the configured drain timeout. A deliberately slow upload may repeatedly prevent reset. WebSocket draining exists for reset but not for normal container shutdown, and outbox claims can remain leased after termination.

#### Required Work

1. Configure trusted-edge and Kestrel header/body-rate and maximum-request-duration controls.
2. Keep maximum upload duration below `DemoReset__DrainTimeoutSeconds`.
3. After a bounded reset grace period, cancel remaining in-flight requests rather than allowing indefinite reset starvation.
4. Add an application shutdown coordinator that:
   - Stops new WebSocket admission.
   - Closes or aborts sockets within the configured timeout.
   - Pauses/drains outbox dispatch.
   - Releases claims held by the terminating dispatcher.
5. Keep total shutdown work below Compose `stop_grace_period`.

#### Acceptance Criteria

- A stalled upload cannot prevent the next reset generation.
- SIGTERM with an open socket and claimed outbox item exits within 30 seconds.
- Restarted instances can immediately dispatch pending outbox work.
- Committed data remains valid after forced cancellation.

## P2 Demo Quality and Reliability

### 8. Complete Credential Revocation for Demo-Created Accounts

Password setup, oath-token rotation, and request removal do not revoke existing bearer sessions. The daily reset eventually removes them, but they remain usable within the current demo day.

#### Required Work

1. Revoke all existing bearer tokens in the same transaction as password replacement, oath-token rotation, or request removal for demo-created accounts.
2. Revalidate or disconnect affected WebSockets promptly.

#### Acceptance Criteria

- Old human and agent bearer tokens fail immediately after credential replacement or removal.
- Existing WebSockets close within a documented bound.

### 9. Close Project Administration Concurrency Gaps

Ticket mutations reauthorize inside immediate write transactions, but project visibility, membership, ownership, and role changes still perform some authorization/invariant checks before their repository transaction.

#### Required Work

1. Move project membership, visibility, owner-transfer, and role invariant checks into their write transactions.
2. Revalidate current project visibility and caller authority under the writer lock.
3. Preserve sensitive-project owner and assignee constraints.
4. Prevent unsupported elevated roles for agent accounts.

#### Acceptance Criteria

- Concurrent project/admin changes cannot commit an invalid sensitive-project state.
- Former owners or newly unscoped seniors cannot complete stale privileged mutations.

### 10. Make Notifications Actionable and Fresh

Notifications are loaded only when the token changes and expose only Mark read:

- `react/src/components/NavBar.jsx:64-90`
- `react/src/components/NavBar.jsx:209-223`

#### Required Work

1. Refresh notifications when the menu opens and when the window regains focus.
2. Add modest authenticated polling or human live updates.
3. Make a ticket notification open its exact `ticketId` through the existing query-state deep-link flow.
4. Mark it read only after successful navigation/opening.
5. Preserve a retryable notification error state.

#### Acceptance Criteria

- New assignment/comment notifications appear without re-login.
- Activating a notification opens the exact ticket.
- Transient fetch failures remain visible and retryable.

### 11. Finish Frontend Accessibility and Resilience

#### Required Work

1. Add a skip link and stable main-content target.
2. Replace remaining decorative spinner `<div>` elements with semantic loading states.
3. Add reduced-motion overrides.
4. Make text-evidence file upload keyboard operable.
5. Add explicit accessible names and error associations where still missing.
6. Prevent dirty report dialogs from closing without confirmation.
7. Prevent dialog dismissal while a mutation is in flight.
8. Add an explicit System theme option alongside light and dark.
9. Remove duplicated authenticated page headings.
10. Add visible View Reports actions to active and archived queues instead of relying only on row menus.
11. Preserve existing list data on transient failures and provide Retry.
12. Fix multi-tab token replacement so an old tab cannot delete a newer tab's session.

#### Acceptance Criteria

- Primary screens have no serious or critical axe findings.
- Keyboard-only navigation, uploads, row actions, notifications, report tabs, and dialogs work.
- Dirty/submitting dialogs cannot lose or obscure work accidentally.
- Loading, error, and success changes are announced appropriately.
- Light, dark, and system modes work and meet WCAG AA contrast.
- Logging into another tab does not erase the new session.

### 12. Make Queue Filters and History Semantics Accurate

Some quick filters operate only on the current cursor page, and Submitted Reports still fetches at most 100 active and 100 closed records without cursor traversal.

#### Required Work

1. Move urgent/recent/date quick filters to server-supported cursor queries with filtered totals, or label them explicitly as current-page filters.
2. Add cursor traversal to Submitted Reports or document it as a bounded demo history view.
3. Ensure bulk/export labels continue to state that they operate on the current page.

#### Acceptance Criteria

- Filter labels, visible records, and totals describe the same scope.
- No advertised queue-wide filter silently ignores later pages.

### 13. Enrich the Deterministic Demo Fixture

The 60-ticket fixture demonstrates metadata and lifecycle distribution but does not seed attachments, text evidence, comments, or unread notifications.

#### Required Work

1. Keep exactly seven users, five projects, and 60 deterministic tickets.
2. Add a small evidence-rich scenario containing:
   - Canonical safe images.
   - Text evidence.
   - Multi-actor comments.
   - Assignment activity.
   - Solution reports.
   - Closed and reopened examples.
   - Read and unread notifications.
3. Document the five-minute role walkthrough against those stable records.

#### Acceptance Criteria

- Reset validation remains deterministic.
- A reviewer can demonstrate the complete collaboration flow immediately after reset.

### 14. Improve Artifact Reproducibility and Initial Load

Docker stages use mutable base tags, NuGet restore is not locked, and the frontend ships one approximately 1.21 MB minified JavaScript bundle.

#### Required Work

1. Pin final release base images by digest.
2. Commit NuGet lock files and restore in locked mode.
3. Record the resolved image digest and SBOM in CI.
4. Lazy-load authenticated pages and heavy Data Grid/report/admin modules.
5. Remove temporary performance console logging.
6. Define a realistic initial-bundle budget and track it in CI.

#### Acceptance Criteria

- Rebuilding from the same inputs resolves the same dependency graph.
- Initial login does not download every authenticated/admin feature.
- No temporary performance logs appear in the browser console.

### 15. Complete Portfolio Presentation and Documentation

#### Required Work

1. Expand `README.md` with:
   - Hosted demo link.
   - Screenshots.
   - Architecture and reset diagram.
   - Five-minute role walkthrough.
   - Security boundary summary.
   - Explicit SQLite/single-replica trade-offs.
2. Add a favicon and basic social/document metadata.
3. Correct stale upload limits in `migration.md`.
4. Reconcile `README.md`, `AUTH.md`, `migration.md`, `design.md`, `demo.md`, and this file.
5. Decide whether priority should be visible by default in work queues.

#### Acceptance Criteria

- A reviewer can understand, run, and evaluate the project without reading `AGENTS.md`.
- Documentation contains no obsolete limits or contradictory demo behavior.
- The built image produces no missing-favicon console error.

## Exact Docker Demo Release Gates

### Build and Unit/Integration Tests

```bash
dotnet build src/BugTracker.Api/BugTracker.Api.csproj --configuration Release
dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj
cd react && npm run build
cd react && npm run test
docker compose config --quiet
docker build --pull --no-cache -t bug-tracker:release-candidate .
```

All commands must pass in CI, not only locally.

### Fresh-Volume Gate

1. Start the exact release-candidate image with a new named volume and a strong random `Auth__TokenSecret`.
2. Run as non-root with a read-only root filesystem.
3. Require `/health/ready` to return `200` within the configured start period.
4. Verify:
   - Migration version 8 is applied.
   - Reset generation is 1.
   - Fixture counts are 7 users, 5 projects, and 60 tickets.
   - `PRAGMA quick_check` returns `ok`.
   - `PRAGMA foreign_key_check` returns no rows.
   - Database, WAL/SHM, and audit files exist only under `/data`.
   - `/app` is not writable.

### Restart and Reset Gate

1. Restart the same image and volume on the same UTC day.
2. Confirm migrations and fixture generation are unchanged.
3. During a due reset, hold an API request, an outbox dispatch, and a WebSocket.
4. Verify maintenance rejects new admission, readiness returns `503`, active work drains or is cancelled within the bound, sockets close, generation increments once, and readiness recovers.
5. Verify old tokens, notifications, audit, attachments, and outbox data are removed.

### Shutdown Gate

1. Open a WebSocket and claim an outbox item.
2. Send SIGTERM.
3. Require exit before the 30-second Compose grace period.
4. Restart immediately and verify pending outbox work is dispatchable without waiting for a stale claim.

### Playwright Core Workflow Gate

Run against the built Docker image and disposable database:

1. First-time visitor discovers demo accounts and warning.
2. Developer logs in, creates a structured ticket with evidence, and opens its report.
3. Senior/admin assigns the ticket.
4. Assignee opens it from Allocated, comments, adds a solution, and closes it.
5. Admin opens the archived report and reopens it.
6. Reopened ticket returns to the active queue.
7. A notification appears without re-login and opens the exact ticket.
8. Manual logout invalidates the previous bearer token.
9. An induced `401` returns to login with the session-expired message.
10. Reset invalidates the browser session cleanly.
11. Logging in from another tab does not erase the new token.

### Routing Gate

Direct-load and refresh:

- `/`
- `/setup-password?token=...&email=...`
- `/?view=tickets&ticket=<id>`
- `/?view=archived&ticket=<id>`

Verify HTML routes return the SPA, unknown `/api/*` routes never return `index.html`, all frontend requests remain same-origin, and no mixed-content or unexpected static-asset failures occur.

### Pagination and Resilience Gate

With more than 120 tickets and tied creation timestamps:

1. Traverse forward/back without duplicates or omissions.
2. Change page sizes between 10, 25, 50, and 100.
3. Change search/filter on page 2+ and verify reset to page 1.
4. Confirm filter totals match filter scope.
5. Inject a transient page failure; existing rows remain and Retry recovers.
6. Log in as another user and verify no cached data leaks across sessions.

### Accessibility and Responsive Gate

Run axe and keyboard tests at:

- `360x800`
- `390x844`
- `768x1024`
- `1024x768`
- `1440x900`

Run primary workflows in light, dark, and system modes. Require:

- No serious or critical axe findings.
- No page-level horizontal overflow.
- No overlapping app bar, drawer, grids, menus, or dialogs.
- Focus trapping and restoration work.
- Skip navigation reaches main content.
- Data Grid footer and report actions remain reachable.
- Zero console errors, unhandled rejections, or unexpected failed requests.

### Public Edge Gate

Before opening Internet access:

1. Set `ASPNETCORE_ENVIRONMENT=Demo`.
2. Inject a deployment-specific secret of at least 32 random bytes.
3. Configure exact `AllowedHosts`, HTTPS `Frontend__Origin`, and trusted proxy IPs.
4. Keep exactly one writable replica with stop-first updates.
5. Mount `/data` on a local filesystem volume with an explicit quota, not NFS/SMB.
6. Expose only the TLS proxy publicly.
7. Confirm WebSocket upgrade, client IP forwarding, security headers, and rate partitions through that proxy.
8. Confirm all public quota violations produce the intended structured response.

## Explicitly Out of Scope for This Demo

These are not public-demo release blockers and should not be reintroduced into this plan unless the deployment begins handling private customer data:

- MFA.
- Multi-organization tenancy.
- Multiple writable replicas.
- PostgreSQL migration.
- External object storage.
- Long-term backup/RPO/RTO automation for disposable data.
- Full production account/session inventory UI.
- Enterprise monitoring and formal SLOs.

## Recommended Work Order

1. Authenticated abuse/storage guardrails and shared-login rate policy.
2. CSV hardening, security headers, and trusted-proxy configuration.
3. Demo-aware login and deterministic evidence-rich scenario.
4. Reset/shutdown hardening.
5. Notifications and frontend resilience/accessibility.
6. CI, exact-image smoke tests, Playwright, and axe gates.
7. Reproducible artifact improvements and portfolio documentation.
