# First Run Setup

Use this procedure for a new installation that is not using `seed-demo`.

## 1. Create the temporary root administrator

Run this command on the host that owns the SQLite database. The command is local-only and is disabled after an administrator exists.

```bash
BUG_TRACKER_BOOTSTRAP_PASSWORD='<temporary-strong-password>' \
  dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- \
  bootstrap-admin --email owner@example.com
```

The temporary password must be at least 12 characters and contain a number and special character. Do not commit it, put it in shell history, or reuse it as the permanent password.

## 2. Sign in and replace the temporary password

Sign in through the web application with the root email and temporary password. The first-run dialog permits only a password replacement. No tickets, users, projects, AI agents, or agent credentials can be created at this point.

After saving a strong replacement password, all root-admin sessions are revoked. Sign in again with the new password.

## 3. Create the first project

The second wizard step creates the first project. The root administrator becomes its owner and is added as a project member. Select sensitive visibility only when every non-admin participant should require explicit membership.

## 4. Set token limits

The final wizard step sets workspace-wide maxima:

- Human bearer session maximum: 15 minutes to 24 hours. The default is 8 hours.
- AI oath-token maximum: 1 to 62 days. The default is 30 days.

The limits apply to new logins and oath-token issuance. Individual AI oath tokens may always be issued for less than the configured maximum.

## 5. Activate the workspace

Completing the token-limit step marks the workspace active. Until then, every AI-agent REST request, agent login, and the agent notification WebSocket return `503` with `errorCode: "setup_incomplete"`.

## Recovery and safety

- There is intentionally no public HTTP endpoint that creates the root administrator.
- If no human administrator exists, use the local `bootstrap-admin` command. It will refuse to run when an administrator already exists.
- Do not delete or manually edit the `first_run_setup` row. It records the server-enforced setup phase and security policy.
- Demo mode is separate. `seed-demo` provisions its own documented demo users and must not be used for production setup.
