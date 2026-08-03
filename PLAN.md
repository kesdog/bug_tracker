# Bug Tracker Product Plan

This plan is for a lightweight bug tracker aimed at small to medium teams, portfolio demonstration, and practical solo-dev use. The product should remain simple, API-first, and accessible to AI agents through normal HTTP flows without requiring MCP.

## Product Goals

- Keep the app clear, fast, and useful for solo development.
- Show enough product depth to work well as a portfolio project.
- Support small-team workflows without becoming a Jira clone.
- Make AI agents first-class users through documented APIs, scoped identity, validation, and logs.
- Keep MCP optional. Agents should be able to interact through REST endpoints and JSON contracts.

## Target Users

- Solo developer: captures bugs, triages quickly, tracks fixes, and keeps a useful archive.
- Small team developer: works assigned tickets, comments, attaches evidence, and closes issues cleanly.
- Senior/admin user: sees broader queues, unassigned work, project health, and user/agent activity.
- AI agent: creates, reads, edits, comments on, and closes tickets through standard endpoints.

## Phase 1: Core Workflow Improvements

### 1. Ticket Comments And Activity Timeline

Implementation status:

- [x] Plain-text ticket comments with timestamps and actor identity.
- [x] Activity timeline shown in ticket report details.
- [x] System-style activity entries for created, edited, assigned, and closed ticket actions.

Add a timeline to each ticket that shows:

- Human comments.
- AI-agent comments.
- System events such as created, edited, assigned, reopened, closed, priority changed, tags changed, and attachments added.
- Clear timestamps and actor identity.

Keep comments simple for the first pass: plain text only.

### 2. Simple Tags

Implementation status:

- [x] `front-end` and `back-end` tags available on ticket creation.
- [x] Tags are returned in list/detail API responses and shown in ticket tables/report details.
- [x] Tags participate in existing-screen search.

Start with required core tags:

- `front-end`
- `back-end`

Allow at most one core area tag on every ticket: `front-end` or `back-end`, never both.

Add optional tags later, such as:

- `regression`
- `blocked`
- `needs-repro`
- `ai-reviewed`
- `security`
- `performance`

Tags should be useful for filtering and dashboard summaries, not a complex taxonomy.

### 3. Priority Separate From Severity

Implementation status:

- [x] Priority field added separately from severity with `p0`, `p1`, `p2`, and `p3` values.
- [x] Priority is stored by the API and shown/sortable in ticket tables.
- [x] Priority participates in ticket search.

Keep severity as impact and add priority as scheduling/ordering.

Severity examples:

- `low`
- `mid`
- `high`
- `urgent`

Priority examples:

- `p0`
- `p1`
- `p2`
- `p3`

This helps small teams decide what to fix first without losing impact context.

Urgent severity is reserved for the highest-impact work and must be paired with `p0` or `p1` priority.

### 4. Role-Aware Saved Views And Filters

Implementation status:

- [x] Quick filters added to existing ticket screens for urgent and recently updated tickets.
- [x] Senior/admin active-ticket view includes an unassigned quick filter.
- [x] Archived tickets include a closed-this-week quick filter.

Add useful quick filters to existing ticket screens.

Possible views:

- My open bugs.
- Urgent.
- Recently updated.
- Closed this week.
- Blocked.
- AI-created bugs.
- Unassigned.

Role rules:

- Admins and seniors can see unassigned queues.
- Regular devs should not see unassigned queues unless explicitly assigned or otherwise authorized.
- AI agents should not see unassigned queues by default.
- AI agents should only see tickets allowed by their token scope, project allocation, or explicit assignment.

### 5. Search On Existing Screens

Implementation status:

- [x] Inline search added to active, allocated, and archived ticket screens.
- [x] Search runs through existing list endpoints and covers title, report text, solution notes, reporter, assignee, project, tags, priority, and severity.

Add search directly to existing ticket screens instead of creating a separate search page.

Search should cover:

- Issue title.
- Description/report text.
- Solution notes.
- Reporter.
- Assignee.
- Project.
- Tags.
- Priority and severity.

Keep this inline and lightweight.

## Phase 2: Structured Bug Reports

### 6. Better Bug Report Fields

Implementation status:

- [x] Human create form includes environment, expected behavior, actual behavior, steps to reproduce, frequency, severity, priority, project, and front-end/back-end tags.
- [x] Create API accepts and validates structured JSON fields, including accepted frequency values.
- [x] Full ticket details return structured fields for humans and agents, while list endpoints stay compact.
- [x] Report detail modal displays structured fields alongside the initial report.

Add easy form fields for humans:

- Summary/title.
- Front-end/back-end tags.
- Environment.
- Expected behavior.
- Actual behavior.
- Steps to reproduce.
- Frequency.
- Severity.
- Priority.
- Project.
- Evidence images/screenshots/text file.

For AI agents, prefer a documented JSON contract with validation over a separate agent-only form.

Agent flow:

- Agent submits JSON to the create/edit endpoint.
- API validates required fields and accepted enum values.
- API returns a clear status: received, valid/invalid, ticket id when created, and field-level validation errors when invalid.
- API stores agent identity and logs the submission.

This is easier for agents than scraping a form and avoids making MCP mandatory.

### 7. Safe Evidence And Attachments

Implementation status:

- [x] Image evidence remains supported through existing report image blocks.
- [x] `.txt` text evidence upload is supported on create with text/plain and extension validation.
- [x] Full ticket details return text evidence; list/dashboard endpoints omit heavy evidence payloads.
- [x] Report detail modal displays expandable text evidence contents.

For the portfolio demo:

- Support images and screenshots already used by report views.
- Allow `.txt` files only for text evidence.
- Avoid general file upload to reduce malware/security risk.
- Make images, screenshots, and text evidence easy to fetch with full ticket details.
- Keep list/dashboard endpoints lightweight so the dashboard loads cleanly.

For personal use later:

- Expand attachment support carefully.
- Add file size limits, content-type validation, storage isolation, and download safety controls.

## Phase 3: Agent-Friendly API

### 8. Simplified REST Endpoints

Implementation scope for next pass:

- Add authenticated API documentation endpoints that any valid bearer token can retrieve, including AI-agent tokens.
- Provide OpenAPI JSON for the implemented API surface.
- Provide human-readable API examples that progress from simple to more complex `curl` and JSON flows.
- Keep examples agent-friendly: compact list first, detail fetch second, explicit validation errors, and no requirement to scrape the UI or use MCP.

Avoid too many specialized endpoints. Keep the API intuitive.

Suggested endpoint groups:

- Fetch/list/search tickets.
- Create ticket.
- Edit ticket.
- Add comment/activity note.
- Close/reopen ticket.

The API should support both humans and agents through the same core resources.

Provide:

- OpenAPI JSON.
- Human-readable API docs.
- Example `curl` requests.
- Clear JSON request/response examples.
- Validation responses that agents can parse.

### 9. Human And AI Activity Logs

Implementation scope for next pass:

- Keep/expand SQLite audit logs as the canonical admin-queryable audit store.
- Also write separate append-only JSONL files for activity by actor type:
- `logs/human-activity.jsonl`
- `logs/agent-activity.jsonl`
- Add admin-only log retrieval with filtering/search.
- Log human login/logout times.
- Log ticket viewed, created, changed, assigned, commented, and closed events with ticket ID.
- Log the same AI-agent events separately when the authenticated actor is an agent.
- Do not write raw auth tokens or API keys to logs.

Log all meaningful activity in general.

Separate log views or filters should distinguish:

- Human activity.
- AI-agent activity.
- System activity.

Log examples:

- Ticket created.
- Ticket edited.
- Comment added.
- Assignment changed.
- Status changed.
- Priority/severity changed.
- Attachment added.
- API validation failure from an agent.
- Agent token used.

This supports trust, debugging, and portfolio storytelling.

### 10. Webhooks And Automation

Keep this as TODO for later.

Future webhook events:

- Ticket created.
- Ticket assigned.
- Ticket closed.
- Urgent bug created.
- AI-agent ticket created or updated.

This should not block the next implementation pass.

## Phase 4: Data Portability And Dashboard

### 11. Import And Export

Implementation scope for next pass:

- Add export controls for `senior` and `admin` users on all ticket screens: active, allocated, and archived.
- Export the rows currently visible after search and quick filters, not manually checked rows.
- Support CSV and JSON export.
- Use a backend export endpoint that validates the caller role and ticket visibility before returning data.
- JSON export includes full ticket details, comments/activity, tags, priority, and evidence metadata.
- CSV export is flat and readable for spreadsheet use.

Import remains later scope.

Start simple:

- Export tickets as JSON.
- Export tickets as CSV.
- Include comments, activity, tags, priority, and evidence metadata in JSON export.
- Keep CSV flat and readable for spreadsheet use.

Later:

- Import JSON with validation.
- Dry-run import mode that returns valid/invalid results before writing.

### 12. Clear Functional Dashboard

Keep the dashboard practical and readable.

Useful widgets:

- My open tickets.
- Urgent tickets.
- Unassigned count for senior/admin only.
- Recently updated tickets.
- Oldest active tickets.
- Closed this week.
- AI-agent activity summary.
- Tickets by front-end/back-end tag.

Avoid overbuilding analytics. The dashboard should answer what needs attention now.

## Recommended Next Implementation Order

1. [x] Add ticket activity timeline and comments.
2. [x] Add simple `front-end` / `back-end` tags plus optional tags.
3. [x] Add priority field.
4. [x] Add inline search/filtering to existing ticket screens with role-aware visibility.
5. [x] Add structured bug report fields for human forms and JSON API validation for agents.
6. [x] Add safe `.txt` evidence support for portfolio demo.
7. Add export to JSON and CSV.
8. Add OpenAPI/API docs and simplify agent-facing endpoint examples.
9. Add human/AI/system activity log filtering.
10. Leave webhooks as a later TODO.

## Positioning Statement

This is a lightweight bug tracker for small teams and solo developers, with structured bug reports, clear ownership, activity history, safe evidence handling, and first-class AI-agent access through documented REST APIs. MCP can be added as an optional integration, but it is not required for agents to use the system.
