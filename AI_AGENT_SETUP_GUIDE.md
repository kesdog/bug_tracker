# AI Agent Setup Guide

This guide shows how to authenticate an AI agent and retrieve only the tickets it is authorized to process.

## 1. Provision An Agent

1. Create or approve an `ai_agent` access request.
2. Assign a unique username and issue an oath token.
3. Store the raw oath token in a secret manager. It is shown only when issued or rotated.

## 2. Login

Exchange the username and oath token for a bearer token:

```bash
curl -sS -X POST "$BASE/api/auth/agent/login" \
  -H 'Content-Type: application/json' \
  -d '{"username":"<USERNAME>","oathToken":"<OATH_TOKEN>"}'
```

Never log the oath token or bearer token.

## 3. Query Relevant Tickets

Humans and agents use the same authorization-scoped list endpoint. Cursor mode is recommended because the legacy array response stops at the requested limit.

```bash
curl -sS "$BASE/api/bugs?status=active&pagination=cursor&limit=25&sort=created_at_desc&projectId=<PROJECT_ID>&severity=high" \
  -H "Authorization: Bearer $AGENT_TOKEN"
```

Supported filters are `search`, `priority`, `severity`, `tag`, `projectId`, `assigneeUserId`, and `reporterUserId`. Follow `nextCursor` until `hasMore` is false. The deterministic order is newest creation time and then ticket ID.

For the authenticated agent's active assignments:

```bash
curl -sS "$BASE/api/bugs/allocated?pagination=cursor&limit=25" \
  -H "Authorization: Bearer $AGENT_TOKEN"
```

## 4. Fetch An Exact Ticket ID

`GET /api/bugs/{id}` is the canonical detail endpoint for both humans and agents:

```bash
curl -sS "$BASE/api/bugs/<TICKET_ID>" \
  -H "Authorization: Bearer $AGENT_TOKEN"
```

The API first resolves that exact ID and then applies the normal ticket/project authorization policy:

- `200`: the agent may read that exact ticket through project membership or a supported exact-ticket rule.
- `404`: the ticket ID does not exist.
- `403` with `errorCode: "ticket_access_denied"`: the ticket exists but the agent lacks permission.

Project ownership never bypasses this check. Agent detail includes usernames and stable IDs but redacts contact email.

## 5. Request Missing Project Access

A structured `403` includes safe reviewer usernames, remediation `steps`, and `requestAccessPath`. Submit that path without changing the ticket ID:

```bash
curl -sS -X POST "$BASE/api/bugs/<TICKET_ID>/access-request" \
  -H "Authorization: Bearer $AGENT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"reason":"Need project membership to investigate this ticket."}'
```

The request is idempotent and does not grant access. A human project owner, eligible senior, or admin must approve it. After approval, retry `GET /api/bugs/<TICKET_ID>`; the same `CanReadTicket` policy still decides access.

## 6. Recommended Work Loop

1. Login and connect to agent notifications when live work is required.
2. Recover unread notifications after reconnecting.
3. Query compact cursor pages or follow a notification's ticket link.
4. Fetch the exact ticket ID before reasoning or mutation.
5. If denied, submit the supplied access-request path and stop work on that ticket until approved.
6. If allowed, inspect version, reports, activity, and attachments.
7. Comment with findings when the ticket cannot be safely progressed.
8. Mutate using the current `expectedVersion`; refetch and reconcile on `409`.
9. Mark the notification read only after handling or documenting the blocker.

## 7. Evidence And Updates

- Report updates use `PATCH /api/bugs/{id}/report`.
- Comments use `POST /api/bugs/{id}/comments`.
- Image attachments use `POST /api/bugs/{id}/attachments` with bounded multipart files.
- Closure uses `PATCH /api/bugs/{id}/close`.
- Always preserve the server-issued ticket version and authorization boundaries.
