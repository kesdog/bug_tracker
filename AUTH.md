Auth Design (MVP)

1) Scope
- Roles supported now: `dev`, `senior`, `admin`.
- Login method: email + password.
- Password storage: hash only (no plain-text password storage).
- Session model: bearer token with 24-hour expiry.
- Email verification: intentionally deferred until later.

2) Tables used
- `users`
  - `user_id` (PK, stable internal identifier)
  - `email` (unique login identity)
  - `password_hash`
  - `role` (`dev`, `senior`, `admin`)
  - `is_active` (0/1)
- `auth_tokens`
  - `token_id` (PK)
  - `user_id` (FK -> `users.user_id`)
  - `token_hash` (hash of issued bearer token; never store raw token)
  - `issued_at`
  - `expires_at`
  - `revoked_at` (nullable)

3) Password hashing approach
- Recommended algorithm in C#: Argon2id (preferred) or BCrypt (acceptable MVP fallback).
- Minimum expectations:
  - Per-password random salt (handled by library).
  - Work factor/cost set to current safe defaults.
  - Store full hash output string in `password_hash`.
- Verification:
  - On login, run verifier against `password_hash`.
  - Never compare passwords directly.

4) Login flow (`POST /api/auth/login`)
- Request body:
  - `email`
  - `password`
- Steps:
  1. Normalize email (trim + lowercase).
  2. Query `users` by email.
  3. If user not found or `is_active = 0`, return 401.
  4. Verify supplied password against `password_hash`.
  5. If invalid, return 401.
  6. Generate cryptographically secure random token (at least 32 bytes).
  7. Hash the token using SHA-256 (or HMAC-SHA-256 with server secret).
  8. Insert into `auth_tokens` with:
     - `token_id`
     - `user_id`
     - `token_hash`
     - `issued_at = now`
     - `expires_at = now + 24h`
  9. Return raw token to client once.
- Response body:
  - `access_token`
  - `expires_at`
  - `user`: `{ user_id, email, role }`

5) Request authentication middleware
- Client sends `Authorization: Bearer <token>`.
- Middleware steps:
  1. Parse bearer token.
  2. Hash incoming token using same hashing method used at login.
  3. Lookup `auth_tokens` by `token_hash`.
  4. Reject if not found.
  5. Reject if `revoked_at` is not null.
  6. Reject if `expires_at <= now`.
  7. Load associated `users` record and reject if `is_active = 0`.
  8. Attach auth context to request:
     - `user_id`
     - `email`
     - `role`

6) Authorization (role checks)
- Policy mapping:
  - `dev`
    - Create bug
    - Close bug
    - Reopen bug
  - `senior`
    - All `dev` permissions
    - Assign bug to `dev`/`senior`
  - `admin`
    - All actions
    - Read audit logs
- Enforce on backend only (frontend checks are for UX, not security).

Project authorization:
- `project_allocations` is the authoritative membership source.
- `normal` projects are organization-wide for admins and seniors. Human and AI-agent `dev` users only discover and create tickets in explicitly allocated projects.
- On normal projects, a reporter or assignee may access that exact ticket without gaining project membership or access to unrelated tickets.
- `sensitive` projects require explicit membership for every non-admin, including seniors, reporters, and assignees.
- Removing a user from a sensitive project immediately removes ticket access.
- Assignment is restricted to human senior/admin callers. AI agents cannot assign even if promoted.
- Normal-project assignments may target active non-members and grant exact-ticket access. Sensitive-project targets must first be added to the project.
- Only human admins may create sensitive projects or change project visibility.
- Every newly created project has one active human admin/senior owner, defaulted to the creator, who is also explicitly allocated to the project. Ownership identifies the primary access contact and does not bypass ticket authorization.
- Project owners cannot be removed from allocations until ownership is transferred. Admins may transfer ownership; a human senior owner may transfer a normal project to another eligible human admin/senior.
- A denied human or AI-agent ticket lookup may create an idempotent project access request. Approval creates authoritative project membership; it never grants ticket access independently of the normal project/ticket policy.
- Full ticket contact email is available only to authorized human callers. AI-agent ticket detail and permission-remediation responses expose usernames and roles, never email.

7) Logout flow (`POST /api/auth/logout`)
- Requires valid bearer token.
- Hash presented token and set `revoked_at = now` in `auth_tokens`.
- Return 204 or 200.

8) Token expiry and cleanup
- Access token lifetime: 24 hours.
- If expired, require login again (no refresh token in MVP).
- Optional cleanup job:
  - Periodically delete rows where:
    - `expires_at < now - 7 days` OR
    - `revoked_at < now - 7 days`

9) Suggested endpoint contract (MVP auth)
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me` (returns authenticated user profile + role)

10) Seed user guidance
- Create at least one user for each role:
  - `dev`
  - `senior`
  - `admin`
- Store only generated password hashes in DB seeds.
- Do not hardcode plain-text passwords in repo.

11) Current code structure
- Startup wiring is in `src/BugTracker.Api/Program.cs`.
- Auth models are in `src/BugTracker.Api/Auth/AuthModels.cs`.
- Password hashing service is in `src/BugTracker.Api/Auth/PasswordHasherService.cs`.
- Token service is in `src/BugTracker.Api/Auth/TokenService.cs`.
- Token/user DB access is in `src/BugTracker.Api/Auth/AuthRepository.cs`.
- Request auth middleware is in `src/BugTracker.Api/Auth/AuthMiddleware.cs`.
- Endpoint handlers are in `src/BugTracker.Api/Auth/AuthEndpoints.cs`.

12) About `dev-only`, `senior-only`, `admin-only` endpoints
- These are not just for error messages.
- They are explicit authorization test endpoints to verify role rules return:
  - `200` for allowed role.
  - `403` for authenticated but forbidden role.
  - `401` for missing/invalid/revoked/expired token.
- They are useful while building; later you can remove them after role checks are enforced on real bug-ticket endpoints.
