# AGENTS Guide for bug_tracker

This file is for coding agents working in this repository.
It documents build/test commands and the coding conventions already used.

## 1) Repository layout

- `src/BugTracker.Api/`: ASP.NET Core minimal API backend (C#, net10.0).
- `react/`: Vite + React frontend.
- `testing/frontend/`: frontend tests (Vitest + Testing Library).
- `testing/backend/BugTracker.Api.Tests/`: backend integration tests (xUnit + WebApplicationFactory).
- `src/BugTracker.Api/Database/Migrations/`: authoritative embedded SQLite schema migrations.
- `bug_tracker.db`: local development SQLite database.
- `instructions.md`, `AUTH.md`: MVP and auth behavior contracts.

## 2) Tooling and stack

- Backend: .NET 10, minimal APIs, `Microsoft.Data.Sqlite`.
- Frontend: React 19, Vite 6, Vitest 3.
- Frontend test libs: `@testing-library/react`, `@testing-library/user-event`, `jsdom`.
- Backend test libs: xUnit, `Microsoft.AspNetCore.Mvc.Testing`.
- Frontend design, React, styling, and frontend testing guidance lives in `design.md`.

## 2a) Fast context for bug-ticket/report work

Use this section before exploring broadly. Most ticket/report changes only need these files.

Frontend:

- App routing/state starts in `react/src/App.jsx`; page names include `tickets`, `allocated`, `archived`, `add-bug`, `projects`, and `requests`.
- Ticket pages:
  - Active list: `react/src/pages/ViewTicketsPage.jsx`.
  - Allocated-to-me list: `react/src/pages/AllocatedPage.jsx`.
  - Closed/archive list: `react/src/pages/ArchivedPage.jsx`.
  - Create form: `react/src/pages/AddBugPage.jsx`.
- Shared ticket/report components:
  - Modal report viewer: `react/src/components/ReportPanel.jsx`.
  - Modal edit/close form: `react/src/components/BugReportFormPanel.jsx`.
  - Allocated options modal: `react/src/components/BugOptionsPanel.jsx`.
  - Sortable table: `react/src/components/TicketTable.jsx`.
  - Block text/image editor: `react/src/components/ReportBuilderEditor.jsx` and `react/src/report_builder.js`.
- API client for bug actions is `react/src/api/bugs.js`; do not inline `fetch` in pages.
- Ticket table helpers and sort accessors live in `react/src/table_utils.js`; add new sortable fields there.
- Existing modal blur/backdrop effect is `.report-overlay` in `react/src/styles/panels.css`; reuse it instead of creating a new overlay pattern.
- Report UI data shape:
  - `description` = initial submitted bug report text.
  - `reportImages` = initial submitted bug report images.
  - `postResolutionReport` / `resolutionNotes` = solution/fix report text.
  - `resolutionReportImages` = solution/fix report images.
  - `assignedAt` = when ticket became active; active time starts here, not at creation.
  - `resolvedByUserId` = user who closed/resolved; fallback to `assigneeUserId` only for old data.
- Allocated page actions:
  - `Edit Bug Report` updates initial report through `updateInitialBugReport`.
  - `Modify Solution Steps` updates solution/fix report through `updateBugReport`.
  - `Close Bug` calls `closeBug` and archives the ticket.
- Archived page should refer to `Reports` plural and use `ReportPanel` with report tabs/focus summary for initial plus solution/fix reports.
- CSS is split by concern under `react/src/styles/`; report/modal/card styling is in `panels.css`, table buttons in `tables.css`, responsive overrides in `responsive.css`.
- Frontend tests for report flows are `testing/frontend/report-panel.test.jsx` and `testing/frontend/allocated-archived-reports.test.jsx`. Prefer targeted runs first:
  - `cd react && npm run test -- ../testing/frontend/report-panel.test.jsx ../testing/frontend/allocated-archived-reports.test.jsx`

Backend:

- Startup/DB migration shim is `src/BugTracker.Api/Program.cs`; add SQLite `ALTER TABLE` compatibility there when adding nullable columns to existing local DBs.
- Bug endpoint routes and authorization are in `src/BugTracker.Api/Bugs/BugEndpoints.cs`.
- Bug SQL/data mapping is in `src/BugTracker.Api/Bugs/BugRepository.cs`.
- Bug DTO/request records are in `src/BugTracker.Api/Bugs/BugModels.cs`.
- Canonical schema is the ordered SQL under `src/BugTracker.Api/Database/Migrations/`; add a new immutable migration together with repository/model changes.
- Current important bug endpoints:
  - `POST /api/bugs` creates a ticket with `description`, optional `reportImages`, and optional `assigneeUserId` for human senior/admin callers. Assigned creation starts as `open`; otherwise it starts as `todo`.
  - `GET /api/bugs?status=active|closed` returns compact list DTOs, not full report details.
  - `GET /api/bugs/{id}` returns full report details/images.
  - `GET /api/bugs/allocated` returns active tickets assigned to the authenticated user.
  - `PATCH /api/bugs/{id}/allocate` assigns a ticket, sets `status = open`, and sets `assigned_at` once.
  - `PATCH /api/bugs/{id}/initial-report` updates the initial submitted report (`description`, `report_images_json`).
  - `PATCH /api/bugs/{id}/report` updates solution/fix report (`post_resolution_report`, `resolution_report_images_json`).
  - `PATCH /api/bugs/{id}/close` closes the ticket, sets `close_date`, `resolved_by_user_id`, solution text, and solution images.
- DB column mapping order matters in `BugRepository.MapBugTicket`; if SELECT columns change, update indexes once and verify with backend tests.
- `BugTicketListItemDto` intentionally omits large text/images; do not add heavy report fields to list endpoints unless explicitly required.
- Permission model for report changes is `CanManageTicket`: admin/senior, reporter, or assignee. Closed tickets cannot edit the initial report.
- Project visibility is `normal` or `sensitive`. Explicit `project_allocations` rows are authoritative membership; ticket participation never grants project-wide access.
- Dev users of either user type can discover and create tickets only in allocated projects. Seniors are organization-wide for normal projects and membership-scoped for sensitive projects. Admins are global.
- Normal-project reporters/assignees retain exact-ticket access. Sensitive-project access always requires membership for non-admins.
- `GET /api/bugs/{id}` is the canonical exact-ticket endpoint for humans and AI agents; it loads the ID and then applies `CanReadTicket`. Inaccessible existing tickets return structured `ticket_access_denied` remediation, while missing IDs return `404`.
- Ticket lists preserve legacy arrays and support `pagination=cursor` with `items`, `totalCount`, `nextCursor`, and `hasMore`; ordering is `(created_at DESC, id DESC)` and page sizes are capped at 100.
- `GET /api/bugs/summary` returns exact authorization-scoped dashboard counts.
- Projects have one active human admin/senior owner defaulted to the creator. Ownership identifies the access contact and never bypasses ticket authorization. Sensitive-project owners must be admins.
- `POST /api/bugs/{id}/access-request` creates an idempotent membership request. Approval creates `project_allocations` membership; agents must retry the same exact-ticket endpoint afterward.
- Backend integration tests live in `testing/backend/BugTracker.Api.Tests/BugEndpointsIntegrationTests.cs`; `SeedBugAsync` mirrors schema columns and must be updated when ticket columns are added.
- Prefer targeted backend runs first:
  - `dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj --filter "FullyQualifiedName~BugEndpointsIntegrationTests"`

Token-saving workflow for common ticket/report edits:

- Read only the relevant page, `ReportPanel.jsx`, `BugReportFormPanel.jsx`, `react/src/api/bugs.js`, `BugEndpoints.cs`, `BugRepository.cs`, `BugModels.cs`, and the two report test files before editing.
- Do not inspect `dist/`, `bin/`, or `obj/` outputs.
- Do not run full test suites until targeted tests pass.
- If changing only labels/CSS, avoid backend reads entirely unless the UI needs fields not currently returned.

## 3) Install commands

- Frontend deps:
  - `cd react && npm install`
- Backend deps:
  - `dotnet restore src/BugTracker.Api/BugTracker.Api.csproj`
  - `dotnet restore testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj`

## 4) Build commands

- Build backend API:
  - `dotnet build src/BugTracker.Api/BugTracker.Api.csproj`
- Build backend tests:
  - `dotnet build testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj`
- Build frontend:
  - `cd react && npm run build`

## 5) Run commands

- Run backend API:
  - `dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj`
- Run frontend dev server:
  - `cd react && npm run dev`

## 5a) Getting API docs/examples from the running API

Use these when an agent needs endpoint docs or copyable request examples without reading backend source first. Set `BASE` to the running backend origin for the current session.

- Set a backend base URL:
  - `BASE=http://127.0.0.1:5040`
  - If your backend uses another port, replace `5040` with the configured API port.
- Get a human bearer token for protected docs endpoints:
  - `TOKEN=$(curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d '{"email":"admin@example.com","password":"AdminPass123!"}' | jq -r '.accessToken')`
- Fetch the OpenAPI-like endpoint summary:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/openapi.json" | jq .`
- Fetch runnable curl/JSON examples:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/examples" | jq .`
- Fetch only the example names:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/examples" | jq -r '.requests[].name'`
- Fetch the AI agent login example:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/examples" | jq -r '.requests[] | select(.name == "Agent login") | .curl'`
- Fetch the agent-friendly image upload example:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/examples" | jq -r '.requests[] | select(.name == "Upload ticket image attachments") | .curl'`
- Fetch the AI agent notification WebSocket example:
  - `curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/docs/examples" | jq -r '.requests[] | select(.name == "Connect agent notification WebSocket") | .command'`
- Agent oath-token login shape returned by the examples endpoint:
  - `curl -s -X POST $BASE/api/auth/agent/login -H 'Content-Type: application/json' -d '{"username":"<USERNAME>","oathToken":"<OATH_TOKEN>"}'`
- Agent ticket image uploads use `multipart/form-data` on `POST /api/bugs/{id}/attachments`; pass local file paths with `-F 'files=@/path/to/image.png;type=image/png'`. The server stores bytes and returns attachment metadata/IDs, while full ticket detail does not include raw image bytes.
- Agent live notifications use `GET /api/agent/notifications/ws` with a WebSocket upgrade and `Authorization: Bearer <AGENT_TOKEN>`. On connect, the server sends `type: "hello"` with unread persisted notifications, `tokenExpiresAt`, `maxDurationSeconds`, heartbeat settings, and `agentInstructions`. The socket lifetime is capped by the agent bearer token expiry, which is bound to the oath-token expiry used at login. The server sends `type: "ping"` every 30 seconds; agents must reply with `{"type":"pong"}`. If pong is not received, the server retries 5 times at 15-second intervals, then closes the socket. Assignment pushes arrive as `type: "ticket.assigned"` with `actionRequired: true`, a `notification` object, `links.ticket`, and `agentInstructions`; new ticket comments arrive as `type: "ticket.commented"` with `kind: "ticket_commented"`. Ticket notifications are work items, not informational pushes: agents must fetch `links.ticket` or `agentInstructions.ticketDetailPath`, inspect the full ticket, and handle/process/deal with it as best they can. If an agent cannot resolve or safely progress the ticket, it must add a comment through `POST /api/bugs/{id}/comments` explaining findings and blockers. Comments are the low-risk fallback because they do not change ticket state and do not overwrite ticket data. After handling, resolving, or documenting the blocker, agents must consume the work item by calling `PATCH agentInstructions.markNotificationReadPath` (equivalent to `PATCH /api/notifications/{id}/read`). Agents should still call `GET /api/notifications?unreadOnly=true` after reconnects to recover and process anything missed while offline.
- WebSocket audit behavior: successful authenticated agent connections log `agent_ws_connected` and `agent_ws_disconnected`. Failed auth logs `agent_ws_auth_failed` with reason `expired_token`, `revoked_token`, `inactive_user`, `missing_bearer`, `empty_bearer`, or `invalid_token` when the token maps to a user or the request includes a matching `userId=<USER_ID>` query parameter or `X-Agent-User-Id` header. Invalid opaque tokens without a matching user hint cannot be attributed to a user and are not written to agent logs.
- Once logged in as an AI agent, verify the issued bearer token:
  - `AGENT_TOKEN=$(curl -s -X POST "$BASE/api/auth/agent/login" -H 'Content-Type: application/json' -d '{"username":"<USERNAME>","oathToken":"<OATH_TOKEN>"}' | jq -r '.accessToken')`
  - `curl -s -H "Authorization: Bearer $AGENT_TOKEN" "$BASE/api/auth/me" | jq .`

## 6) Test commands (full)

- Frontend full suite:
  - `cd react && npm run test`
- Backend full suite:
  - `dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj`

## 7) Test commands (single test)

- Frontend: run one test file:
  - `cd react && npm run test -- ../testing/frontend/login-page.test.jsx`
- Frontend: run by test name pattern:
  - `cd react && npm run test -- -t "signs in and shows session card on success"`
- Backend: run one test method by name:
  - `dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj --filter "Name~CreateBugWithAuthorization_Returns201AndTodoTicket"`
- Backend: run one test class:
  - `dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj --filter "FullyQualifiedName~BugEndpointsIntegrationTests"`

## 8) Lint/format commands

- Frontend lint: no dedicated lint script is currently configured in `react/package.json`.
- Backend lint: no dedicated lint/analyzer command is configured beyond compiler warnings.
- Optional formatting (if available in environment):
  - `dotnet format src/BugTracker.Api/BugTracker.Api.csproj`
- Keep changes consistent with existing formatting even when no formatter is enforced.

## 9) Database workflow commands

- Inspect tables:
  - `sqlite3 bug_tracker.db "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"`
- Apply migrations to a new database:
  - `Database__Path=/absolute/path/to/my_temp.db dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- migrate`
- Seed a disposable demo database:
  - `Database__Path=/absolute/path/to/demo.db dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- seed-demo`

## 10) Existing contract constraints

- Treat `instructions.md` and `AUTH.md` as behavioral contracts for MVP.
- Auth middleware protects `/api/*` except `/api/auth/login`.
- API should preserve status semantics in docs:
  - Active: `todo`, `open`, `reopened`
  - Archive: `closed`

## 11) C# backend style guidelines

- Use file-scoped namespaces (current code uses `namespace X;`).
- Prefer `sealed` for service classes unless inheritance is needed.
- Prefer immutable records for request/response DTOs.
- Use explicit constructor injection via minimal API handler parameters and DI.
- Keep endpoint handlers thin; place SQL/data logic in repository classes.
- Use async database operations and pass `CancellationToken` through the stack.
- Use parameterized SQL only (`$param` bindings); never string-concatenate user input into SQL.
- Normalize and validate incoming strings before use (`Trim`, lowercase for enums/emails).
- For API errors, return structured JSON with `error` field and appropriate status codes.
- Prefer early returns for invalid/unauthorized states.
- Time handling:
  - Use UTC (`DateTimeOffset.UtcNow`).
  - Persist SQLite datetimes in `yyyy-MM-dd HH:mm:ss` format used by existing code.
- Security:
  - Never store raw auth tokens in DB.
  - Never compare secrets with non-constant-time methods when applicable.
- Naming:
  - Private fields: `_camelCase`
  - Locals/parameters: `camelCase`
  - Types/methods/properties: `PascalCase`

## 12) Testing guidelines

- Frontend: see `design.md`.
- Backend:
  - Prefer integration tests through HTTP pipeline for auth + endpoint behavior.
  - Use isolated temp SQLite DBs in tests; do not mutate `bug_tracker.db`.
  - Seed only minimal required fixtures.
  - Assert both status code and key response payload fields.

## 13) Error handling guidelines

- Return `401` for missing/invalid auth.
- Return `403` for authenticated-but-forbidden role checks.
- Return `400` for invalid request shape/value.
- Return `201` for successful resource creation with `Location` header when applicable.
- Avoid swallowing exceptions silently in repositories/services.

## 14) Agent workflow expectations

- Before editing, read related files and follow existing local patterns.
- Make focused changes; avoid broad refactors unless requested.
- Run relevant tests after changes:
  - frontend changes -> `cd react && npm run test`
  - backend changes -> `dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj`
- If both layers change, run both suites.
- Do not commit secrets, token values, or plain-text credentials.

## 15) Cursor/Copilot rules check

- No `.cursorrules` file found.
- No `.cursor/rules/` directory found.
- No `.github/copilot-instructions.md` found.
- If these files are added later, merge their instructions into this guide.
