# Public demo accounts

> **Public-demo warning:** These credentials are intentionally public and provide access only to disposable demonstration data. Never reuse these passwords, enter personal/confidential information, or enable this fixture against a production or shared business database.

| Username | Email | Password | Role | Project access |
| --- | --- | --- | --- | --- |
| `admin` | `admin@example.com` | `AdminPass123!` | Admin | All five projects, including sensitive `socket manager` |
| `alex.senior` | `alex.senior@example.com` | `SeniorPass123!` | Senior | `bugtracker`, `currency & metal converter`, `website (personal)` |
| `morgan.senior` | `morgan.senior@example.com` | `SeniorPass123!` | Senior | `reservation system`, sensitive `socket manager` |
| `ava.dev` | `ava.dev@example.com` | `DevPass123!` | Developer | `bugtracker`, `website (personal)` |
| `noah.dev` | `noah.dev@example.com` | `DevPass123!` | Developer | `currency & metal converter`, `website (personal)` |
| `mia.dev` | `mia.dev@example.com` | `DevPass123!` | Developer | `reservation system`, sensitive `socket manager` |
| `liam.dev` | `liam.dev@example.com` | `DevPass123!` | Developer | `bugtracker`, `currency & metal converter`, `reservation system` |

The fixture contains exactly seven active human users and no agents. It creates five projects with 12 realistic tickets each. Every project has four todo, four open, one reopened, and three closed tickets. Ticket dates are generated relative to the reset clock and cover at most the preceding 27 days.

## Requesting disposable access

In the hosted `Demo` environment, the public access-request form accepts any email address that passes the displayed format validation for identification. Use a fictitious address, such as `visitor.river@example.test`; no email is sent by this demo. Do not submit personal, confidential, or deliverable email addresses. The same behavior supports both regular-user (`human`) and agent (`ai_agent`) requests.

Requests still require approval by an administrator. Because the demo administrator credentials are public, another visitor can review, alter, approve, or remove pending requests before you do; treat the workflow and any resulting account or oath token as disposable and untrusted. A daily reset can also remove the request or account at any time. Use a fresh unique alias after a collision or reset, and never reuse demo credentials or tokens elsewhere.

## Reset and session expectations

The hosted public demo is expected to reset daily. A reset replaces all demo business data, increments the fixture generation, and invalidates existing login sessions by deleting authentication tokens. Any edits, comments, uploads, notifications, or tickets created during a session are disposable. After a reset, sign in again with an account above; stale ticket links and IDs from the previous generation will no longer resolve.

## Commands and reset safety

- `seed-demo` creates the fixture only when all business/runtime tables are empty.
- Programmatic resets use `DemoResetService.ResetAsync(options, environment, cancellationToken)`.
- Reset is denied unless `DemoResetOptions.Enabled` is `true` and the caller-supplied environment exactly matches an entry in `AllowedEnvironments` (case-insensitive).
- A reset atomically deletes child rows first, recreates and validates the fixture, and increments the persistent generation in `demo_reset_state`. `schema_migrations` and `demo_reset_state` are preserved.

Never expose infrastructure credentials, connection strings, signing keys, provider tokens, or other secrets in this document. Never enable demo reset or retain these accounts in a production or shared business environment.
