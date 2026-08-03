Bug Tracker System (MVP)

1) What MVP means here
- MVP = minimum useful version that can be used end-to-end by the team.
- For this project, MVP includes:
  - User login with email + password (hashed).
  - Submit bug tickets.
  - View active tickets on dashboard.
  - Close and reopen tickets.
  - Separate archive view (closed tickets).
  - Basic role-based permissions.
  - Admin-visible audit log.
- MVP does not include (v1.1+):
  - Image/file attachments.
  - Advanced reporting formats.
  - Complex analytics/performance tuning.
  - Advanced auth/session improvements.

2) Core product idea
- Track active bugs and archived (closed) bugs.
- Backend: C# API.
- Frontend: React, usable on desktop and mobile.
- Database: SQLite for now.

3) Roles and permissions
- `dev`
  - Can submit a bug.
  - Can set bugs to `closed`.
  - Can reopen bugs.
- `senior`
  - All `dev` permissions.
  - Can assign bugs to `dev` and `senior` users.
- `admin`
  - Can see everything.
  - Can view audit logs.

Project visibility and membership:
- Projects have `normal` or `sensitive` visibility.
- `project_allocations` is the only source of project membership.
- Dev users, whether human or AI agents, can discover and create tickets only in allocated projects.
- Seniors have organization-wide access to normal projects but require membership for sensitive projects.
- Admins have organization-wide access to all projects.
- Normal-project reporters and assignees may access their exact ticket without receiving project-wide access.
- Sensitive projects have no reporter/assignee exception after membership is removed.
- Only human senior/admin users can assign. Sensitive-project targets must be members first.
- Projects have one active human admin/senior owner. Project creation defaults ownership to the logged-in creator and explicitly allocates that owner.
- Ownership identifies the primary access contact but does not grant a separate ticket-authorization bypass.
- Humans and AI agents that cannot reach a known ticket may request project access; an authorized human approval adds project membership before the same ticket ID is retried.

4) Auth rules (simple first pass)
- Login with email + password.
- Store password hashes (no plain text passwords).
- Token/session lifetime target: 24 hours.
- Logout + strict token-expiry handling can be finalized after core flow is stable.

5) Ticket lifecycle and behavior
- Status enum: `todo`, `open`, `closed`, `reopened`.
- `closed` means archived:
  - Do not show on main dashboard.
  - Show in archive screen.
- Reopen flow:
  - Change status from `closed` to `reopened` (or `open`, based on UI choice).
  - Allow editing description when reopening.
- Closing a ticket sets `close_date`.
- Reopening a ticket clears `close_date`.

6) User workflow (fleshed out)
- User logs in.
- User opens "Create Bug" form and enters required fields.
- Frontend validates using reusable validation rules.
- If valid, submit to backend.
- Backend validates again and stores record in SQLite.
- A human senior/admin may optionally provide `assigneeUserId`; assigned creation starts in `open` status and sets `assigned_at`.
- Dashboard default view:
  - Fetch most recent 10 active tickets (`todo`, `open`, `reopened`).
  - Sort by newest `created_at` first.
- Archive view:
  - Fetch `closed` tickets only.
- Reopen action:
  - User reopens a closed ticket.
  - User can update description and add/update post-resolution report text.
  - Ticket returns to active list.

7) Data model (SQLite)
- `id` (string, primary key)
  - Demo format: portion of datetime + submitter + title.
  - If collision happens, append a short suffix.
- `issue_title` (string, required)
- `description` (text, required)
- `bug_type` (enum: `page_not_loading`, `form_submission`, `crash`, `api`, `database`)
- `reporter_user_id` (string, required, FK -> `users.user_id`)
- `created_at` (datetime, required)
- `updated_at` (datetime, required)
- `status` (enum: `todo`, `open`, `closed`, `reopened`)
- `assignee_user_id` (string, nullable, FK -> `users.user_id`)
- `severity` (enum: `low`, `mid`, `high`, `urgent`)
- `close_date` (datetime, nullable; null on creation)
- `resolution_notes` (text, nullable)
- `post_resolution_report` (text, nullable; text-only in MVP)

8) Audit log (admin-visible)
- Capture key events:
  - ticket created
  - status changed
  - assignment changed
  - description edited on reopen
- Suggested fields:
  - `audit_id`, `ticket_id`, `actor_user_id`, `action`, `before_json`, `after_json`, `created_at`

9) API examples (initial)
- `POST /api/auth/login`
- `POST /api/bugs`
- `GET /api/bugs?status=active&limit=10&sort=created_at_desc` (dashboard default)
- `GET /api/bugs?status=closed` (archive)
- `PATCH /api/bugs/{id}/status`
- `PATCH /api/bugs/{id}` (description, assignee, severity, post-resolution report)
- `GET /api/audit-logs` (admin only)

10) UI behavior notes
- Forms must support keyboard tab flow.
- Dashboard shows loading spinner while query is in progress.
