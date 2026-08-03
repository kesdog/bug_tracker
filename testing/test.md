# Frontend Testing Suite

## Recommended Suite for This Project
Use **Vitest + React Testing Library** for frontend tests.

Why this choice:
- Fast local feedback loop for React components.
- Native Vite integration (clean setup, low config overhead).
- Focuses on behavior a user sees (form validation, login success/failure, loading/error states).
- Easy to mock API calls while backend is still evolving.

For later (when flows are stable), add **Playwright** for full end-to-end browser tests.

## Current Test Coverage

### 1) `testing/frontend/auth-client.test.js`
What it tests:
- Successful `/api/auth/login` response parsing.
- Friendly error mapping for `401` invalid credentials.
- Successful `/api/auth/me` profile retrieval with bearer token.

Why it matters:
- Keeps API-client behavior stable as backend contracts evolve.
- Ensures auth failures return user-friendly messages.

### 2) `testing/frontend/login-page.test.jsx`
What it tests:
- Empty login form shows validation message.
- Valid login flow updates UI to authenticated state.
- Access token is stored in `localStorage` for session reuse.

Why it matters:
- Protects the core login UX from regressions.
- Confirms keyboard form flow can submit through the real form behavior.

## How to Run
From the `react` directory:

```bash
npm install
npm run test
```

## Suggested Next Tests
1. Login API failure (`500`) shows generic retry message.
2. Existing token on load fetches `/api/auth/me` and restores session.
3. Logout clears token and returns to login form.

## Backend Integration Coverage (`testing/backend/BugTracker.Api.Tests`)

### Test stack and isolation
- Uses **xUnit + WebApplicationFactory/TestServer** for real HTTP integration checks against the ASP.NET Core pipeline.
- Each test class gets its own temporary SQLite DB file created under the system temp directory.
- DB schema is applied by `SqliteMigrationRunner`, then isolated test users are seeded with PBKDF2 password hashes.
- Test factory overrides `Database:Path` and `Auth:TokenSecret` via in-memory configuration, so tests never touch `bug_tracker.db`.

### 1) `GetBugsWithoutBearerToken_Returns401Unauthorized`
What it tests:
- Calls `GET /api/bugs` without auth.
- Verifies middleware enforcement returns `401`.

How it works:
- Sends a plain request with no `Authorization` header.
- Asserts HTTP status code directly.

### 2) `CreateBugWithAuthorization_Returns201AndTodoTicket`
What it tests:
- Valid bearer token flow.
- `POST /api/bugs` creates ticket and returns `201` payload.

How it works:
- Seeds a real token hash into `auth_tokens` in the isolated DB, then sends the matching raw bearer token.
- Posts a valid bug payload.
- Asserts expected ticket fields and default `status = todo`.

### 3) `GetActiveBugs_ReturnsCreatedTicket`
What it tests:
- Active-list query includes newly created active ticket.

How it works:
- Creates a ticket via API.
- Calls `GET /api/bugs?status=active`.
- Asserts returned array contains the created ticket id.

### 4) `GetClosedBugs_ReturnsOnlyClosedTickets`
What it tests:
- Archive query behavior for closed tickets only.

How it works:
- Seeds one `closed` and one `open` ticket directly into isolated DB.
- Calls `GET /api/bugs?status=closed`.
- Asserts only closed tickets are returned.

### 5) `CreateBugWithInvalidEnums_Returns400`
What it tests:
- Validation gates for `bug_type` and `severity` enum values.

How it works:
- Runs theory cases with invalid `bug_type` and invalid `severity`.
- Asserts `400` and the matching error message.

### 6) `CreateAndListBugs_AverageCase_PerformanceUnder500Ms`
What it tests:
- Average-case baseline latency for create + list flow.

How it works:
- Measures elapsed wall-clock time for one `POST /api/bugs` and one `GET /api/bugs?status=active&limit=10&sort=created_at_desc`.
- Asserts both responses succeed and total duration is below 500ms in the local integration environment.

### Security considerations covered by tests
- Auth middleware requires bearer token before any `/api/bugs` access.
- Validation rejects unsupported enum inputs at API boundary.
- Repository queries use parameterized SQL, and tests exercise normal and invalid input paths through that layer.

### Run backend tests
From repository root:

```bash
dotnet test testing/backend/BugTracker.Api.Tests/BugTracker.Api.Tests.csproj
```
