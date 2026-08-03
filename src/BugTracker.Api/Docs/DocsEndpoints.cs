namespace BugTracker.Api.Docs;

public static class DocsEndpoints
{
    public static IEndpointRouteBuilder MapDocsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/docs/openapi.json", GetOpenApi);
        app.MapGet("/api/docs/examples", GetExamples);
        return app;
    }

    private static IResult GetOpenApi()
    {
        var document = new
        {
            openapi = "3.0.3",
            info = new { title = "BugTracker API", version = "1.0.0-mvp" },
            ticketConcurrency = new
            {
                versionField = "Ticket detail and list items include version.",
                mutationRule = "Assign, every bulk assignment item, metadata, initial report, solution report, close, reopen, and multipart attachment mutations require expectedVersion. Comments are append-only and exempt.",
                missingVersion = new { status = 428, errorCode = "ticket_version_required" },
                staleVersion = new { status = 409, errorCode = "ticket_version_conflict", recovery = "Refetch, resolve/merge, and retry with currentVersion." }
            },
            security = new[] { new Dictionary<string, string[]> { ["bearerAuth"] = [] } },
            components = new
            {
                securitySchemes = new
                {
                    bearerAuth = new { type = "http", scheme = "bearer", bearerFormat = "opaque" }
                }
            },
            paths = new Dictionary<string, object>
            {
                ["/api/auth/login"] = new { post = new { summary = "Human login", security = Array.Empty<object>() } },
                ["/api/auth/agent/login"] = new { post = new { summary = "AI agent oath-token login", security = Array.Empty<object>() } },
                ["/api/auth/logout"] = new { post = new { summary = "Revoke current bearer token" } },
                ["/api/auth/me"] = new { get = new { summary = "Current user profile" } },
                ["/api/auth/users/{userId}/username"] = new { patch = new { summary = "Normalize and update a unique username; human admin only" } },
                ["/api/bugs"] = new
                {
                    get = new { summary = "List scoped active or closed tickets; legacy mode returns an array and pagination=cursor returns an envelope", parameters = new[] { "status=active|closed", "limit=1..100", "search", "priority=p0|p1|p2|p3", "severity=low|mid|high|urgent", "tag", "projectId", "assigneeUserId", "reporterUserId", "sort=created_at_desc", "pagination=cursor", "cursor=<opaque>" }, cursorResponse = new { items = "compact ticket array", totalCount = 0, nextCursor = "opaque or null", hasMore = false } },
                    post = new { summary = "Create a ticket; title max 200, initial report max 20,000, environment/expected/actual/steps max 500/2,000/2,000/4,000, up to 3 validated PNG/JPEG/WebP images, and up to 3 text/plain evidence files (100,000 UTF-8 bytes each, 300,000 aggregate; names max 80)" }
                },
                ["/api/bugs/{id}"] = new { get = new { summary = "Canonical exact-ID detail for humans and agents; authorization always uses exact ticket CanReadTicket rules", notes = "Existing inaccessible tickets return structured 403 remediation without ticket content; missing IDs return 404. Human details may include contact email; agent details never include email." } },
                ["/api/bugs/summary"] = new { get = new { summary = "Exact uncapped scoped counts for active, allocated, visible projects, urgent, unassigned, and statuses" } },
                ["/api/bugs/allocated"] = new { get = new { summary = "Current user's active assignments; cursor mode returns compact items while legacy mode preserves full-ticket array", parameters = new[] { "pagination=cursor", "cursor=<opaque>", "limit=1..100", "sort=created_at_desc", "all ticket filters" } } },
                ["/api/bugs/{id}/access-request"] = new { post = new { summary = "Idempotently request project membership for an inaccessible exact ticket; never grants access" } },
                ["/api/bugs/{id}/metadata"] = new { patch = new { summary = "Update ticket metadata; expectedVersion is required" } },
                ["/api/bugs/{id}/comments"] = new { post = new { summary = "Add a ticket comment (maximum 2,000 characters)" } },
                ["/api/bugs/{id}/attachments"] = new { post = new { summary = "Upload up to 3 single-frame PNG/JPEG/WebP images using multipart/form-data; content-derived MIME, full decode, metadata-stripped canonical storage, 4 MiB decoded each, 12 MiB aggregate, orientation-neutral 3840x2160 and 8,294,400 pixels; expectedVersion is required" } },
                ["/api/bugs/{id}/attachments/{attachmentId}"] = new { get = new { summary = "Download one readable ticket attachment on demand" } },
                ["/api/bugs/{id}/allocate"] = new { patch = new { summary = "Assign a ticket; human senior/admin only; expectedVersion is required" } },
                ["/api/bugs/bulk-allocate"] = new { patch = new { summary = "Assign tickets; every items entry requires expectedVersion; legacy ticketIds-only requests return 428" } },
                ["/api/projects"] = new
                {
                    get = new { summary = "List projects visible under normal/sensitive visibility rules" },
                    post = new { summary = "Create a normal project as senior/admin or a sensitive project as a human admin" }
                },
                ["/api/projects/{projectId}/visibility"] = new { patch = new { summary = "Change normal/sensitive project visibility; human admin only" } },
                ["/api/projects/{projectId}/owner"] = new { patch = new { summary = "Transfer ownership to an active human senior/admin; sensitive projects require admin" } },
                ["/api/projects/access-requests"] = new { get = new { summary = "List reviewable project access requests" } },
                ["/api/projects/access-requests/{requestId}"] = new { patch = new { summary = "Approve or deny a pending request; approval transactionally creates membership" } },
                ["/api/bugs/{id}/initial-report"] = new { patch = new { summary = "Update submitted report (maximum 20,000 characters and 3 validated images); expectedVersion is required" } },
                ["/api/bugs/{id}/report"] = new { patch = new { summary = "Update solution/fix report (maximum 20,000 merged characters and 3 validated images); expectedVersion is required" } },
                ["/api/bugs/{id}/close"] = new { patch = new { summary = "Close a ticket with resolution notes (maximum 20,000 characters and 3 validated images); expectedVersion is required" } },
                ["/api/bugs/{id}/reopen"] = new { patch = new { summary = "Reopen a closed ticket with a reason of at most 1,000 characters; expectedVersion is required" } },
                ["/api/bugs/export"] = new { post = new { summary = "Export selected readable tickets as json or csv; senior/admin humans only" } },
                ["/api/notifications"] = new { get = new { summary = "List current user's notifications", parameters = new[] { "unreadOnly=true|false" } } },
                ["/api/notifications/unread-count"] = new { get = new { summary = "Count current user's unread notifications" } },
                ["/api/notifications/read-all"] = new { patch = new { summary = "Mark all current user's notifications read" } },
                ["/api/notifications/{id}/read"] = new { patch = new { summary = "Mark one owned notification read" } },
                ["/api/agent/notifications/ws"] = new
                {
                    get = new
                    {
                        summary = "Authenticated AI-agent WebSocket for live notification events",
                        notes = new[]
                        {
                            "Requires WebSocket upgrade and Authorization: Bearer <AGENT_TOKEN> header.",
                            "For auditable failed auth attempts with an invalid opaque token, include userId=<USER_ID> query or X-Agent-User-Id header; logs are only attributed when that user id exists.",
                            "Human users receive 403. Non-upgrade requests receive 400 for agent tokens.",
                            "On connect, server sends type=hello with unread notifications, tokenExpiresAt, maxDurationSeconds, and heartbeat settings.",
                            "Connection lifetime is capped by the agent bearer token expiry, which is bound to the oath-token expiry used at login.",
                            "Server sends ticket.assigned, ticket.closed, ticket.reopened, ticket.commented, or notification.created events.",
                            "Each live event includes top-level eventId and ticketVersion. Deduplicate eventId and ignore an event whose ticketVersion is older than the version already observed for that ticket.",
                            "All ticket aggregate mutations require expectedVersion from the latest detail/list response. Missing versions return 428; stale versions return 409.",
                            "On HTTP 409, refetch the ticket, resolve and merge concurrent changes, then retry with the current version. Never blindly replay a stale mutation.",
                            "Ticket notifications are action-required work items, not informational pushes: agents must fetch links.ticket or agentInstructions.ticketDetailPath, inspect the ticket, and handle/process/deal with it as best they can.",
                            "If an agent cannot resolve or safely progress a ticket, it must POST a comment explaining findings/blockers; comments are the low-risk fallback because they do not change ticket state or overwrite ticket data.",
                            "After handling, resolving, or documenting the ticket with a blocker comment, agents must call PATCH agentInstructions.markNotificationReadPath so the notification is consumed and leaves the unread work queue.",
                            "Server sends {\"type\":\"ping\"} every 30 seconds. Agent must respond with {\"type\":\"pong\"}.",
                            "If no pong is received, server retries ping 5 times at 15-second intervals, then closes the socket.",
                            "Clients can also send ping or {\"type\":\"ping\"}; server responds with type=pong."
                        }
                    }
                },
                ["/api/audit-logs"] = new { get = new { summary = "Query canonical audit logs; admin humans only", parameters = new[] { "actorType=human|agent", "search", "ticketId", "action", "limit=1..500" } } },
                ["/api/docs/openapi.json"] = new { get = new { summary = "This OpenAPI-like MVP document" } },
                ["/api/docs/examples"] = new { get = new { summary = "Curl and JSON examples" } }
            }
        };

        return Results.Ok(document);
    }

    private static IResult GetExamples()
    {
        var examples = new
        {
            note = "Examples use placeholder values only. Ticket detail/list responses include version. Every ticket mutation except append-only comments requires expectedVersion; fetch the latest ticket first.",
            baseUrl = "http://127.0.0.1:5000",
            requests = new object[]
            {
                new
                {
                    name = "Human login",
                    curl = "curl -s -X POST $BASE/api/auth/login -H 'Content-Type: application/json' -d '{\"email\":\"user@example.com\",\"password\":\"<PASSWORD>\"}'"
                },
                new
                {
                    name = "Agent login",
                    curl = "curl -s -X POST $BASE/api/auth/agent/login -H 'Content-Type: application/json' -d '{\"username\":\"<USERNAME>\",\"oathToken\":\"<OATH_TOKEN>\"}'"
                },
                new
                {
                    name = "List active tickets",
                    curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/bugs?status=active&limit=25&search=checkout'"
                },
                new
                {
                    name = "Cursor page with filters",
                    curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/bugs?status=active&pagination=cursor&limit=100&sort=created_at_desc&severity=high&projectId=<PROJECT_ID>&cursor=<OPTIONAL_NEXT_CURSOR>'",
                    response = new { items = Array.Empty<object>(), totalCount = 0, nextCursor = (string?)null, hasMore = false }
                },
                new { name = "Get scoped ticket summary", curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/bugs/summary'" },
                new
                {
                    name = "Get ticket detail",
                    curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/bugs/<TICKET_ID>'"
                },
                new { name = "Agent exact-ID access", curl = "curl -H 'Authorization: Bearer <AGENT_TOKEN>' '$BASE/api/bugs/<TICKET_ID>'", note = "Returns 200 only when CanReadTicket permits exact-ticket/project access; 403 includes requestAccessPath, safe reviewer usernames/roles, and no ticket content or email." },
                new { name = "Request ticket project access", curl = "curl -X POST $BASE/api/bugs/<TICKET_ID>/access-request -H 'Authorization: Bearer <TOKEN>' -H 'Content-Type: application/json' -d '{\"reason\":\"Need project membership to investigate this ticket.\"}'" },
                new { name = "Review project access request", curl = "curl -X PATCH $BASE/api/projects/access-requests/<REQUEST_ID> -H 'Authorization: Bearer <HUMAN_REVIEWER_TOKEN>' -H 'Content-Type: application/json' -d '{\"status\":\"approved\",\"reviewNote\":\"Approved for investigation.\"}'" },
                new
                {
                    name = "Create valid ticket",
                    curl = "curl -X POST $BASE/api/bugs -H 'Authorization: Bearer <TOKEN>' -H 'Content-Type: application/json' -d @ticket.json",
                    json = new
                    {
                        issueTitle = "Checkout button spins forever",
                        description = "The payment confirmation never completes.",
                        bugType = "form_submission",
                        projectId = "project-general",
                        severity = "high",
                        priority = "p1",
                        tags = new[] { "front-end", "regression" },
                        assigneeUserId = "<OPTIONAL_ACTIVE_USER_ID_FOR_HUMAN_SENIOR_OR_ADMIN>",
                        textEvidence = new[] { new { name = "console.txt", contentType = "text/plain", text = "Use short test evidence only." } }
                    },
                    note = "Omit assigneeUserId for an unassigned todo ticket. Assigned creation produces an open ticket. Sensitive-project assignees must already be project members."
                },
                new
                {
                    name = "Validation failure: urgent + p2",
                    expectedStatus = 400,
                    json = new { issueTitle = "Urgent invalid priority", description = "Should fail.", bugType = "api", projectId = "project-general", severity = "urgent", priority = "p2" }
                },
                new
                {
                    name = "Add comment",
                    curl = "curl -X POST $BASE/api/bugs/<TICKET_ID>/comments -H 'Authorization: Bearer <TOKEN>' -H 'Content-Type: application/json' -d '{\"body\":\"Investigating repro steps.\",\"recipientUserId\":\"<OPTIONAL_RELEVANT_CONTACT_ID>\"}'"
                },
                new
                {
                    name = "Upload ticket image attachments",
                    curl = "curl -X POST $BASE/api/bugs/<TICKET_ID>/attachments -H 'Authorization: Bearer <TOKEN>' -F 'expectedVersion=<CURRENT_VERSION>' -F 'purpose=solution-report' -F 'files=@/tmp/screenshot-1.png;type=image/png' -F 'files=@/tmp/screenshot-2.png;type=image/png'",
                    note = "Use the version from the latest ticket response. Use purpose initial-report, solution-report, or close-report. Per report, the API accepts at most 3 single-frame PNG/JPEG/WebP images, 4 MiB decoded each and 12 MiB aggregate, no larger than orientation-neutral 3840x2160 or 8,294,400 pixels. Declared MIME must match decoded content; accepted images are re-encoded to strip metadata."
                },
                new
                {
                    name = "Download ticket attachment",
                    curl = "curl -L -H 'Authorization: Bearer <TOKEN>' '$BASE/api/bugs/<TICKET_ID>/attachments/<ATTACHMENT_ID>' -o attachment.png"
                },
                new
                {
                    name = "Close ticket",
                    curl = "curl -X PATCH $BASE/api/bugs/<TICKET_ID>/close -H 'Authorization: Bearer <TOKEN>' -H 'Content-Type: application/json' -d '{\"resolutionNotes\":\"Fixed and verified in staging.\",\"expectedVersion\":<CURRENT_VERSION>}'",
                    note = "If this returns 409, refetch the ticket, merge/resolve concurrent changes, and retry using the returned currentVersion."
                },
                new
                {
                    name = "List unread notifications",
                    curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/notifications?unreadOnly=true'"
                },
                new
                {
                    name = "Count unread notifications",
                    curl = "curl -H 'Authorization: Bearer <TOKEN>' '$BASE/api/notifications/unread-count'"
                },
                new
                {
                    name = "Mark all notifications read",
                    curl = "curl -X PATCH $BASE/api/notifications/read-all -H 'Authorization: Bearer <TOKEN>'"
                },
                new
                {
                    name = "Agent consume handled notification",
                    curl = "curl -X PATCH $BASE/api/notifications/<NOTIFICATION_ID>/read -H 'Authorization: Bearer <AGENT_TOKEN>'",
                    note = "Agents should call this only after they have fetched the ticket, handled/resolved it, or left a blocker comment. Marking read consumes the work item and removes it from GET /api/notifications?unreadOnly=true."
                },
                new
                {
                    name = "Connect agent notification WebSocket",
                    command = "BASE_WS=ws://127.0.0.1:5000 && websocat -H='Authorization: Bearer <AGENT_TOKEN>' $BASE_WS/api/agent/notifications/ws?userId=<AGENT_USER_ID>",
                    audit = "Successful connections log agent_ws_connected and agent_ws_disconnected. Failed auth logs agent_ws_auth_failed with reason expired_token, revoked_token, inactive_user, missing_bearer, empty_bearer, or invalid_token when the token maps to a user or the provided userId/X-Agent-User-Id matches a user.",
                    hello = new
                    {
                        type = "hello",
                        userId = "usr_agent_001",
                        unread = Array.Empty<object>(),
                        tokenExpiresAt = "2026-07-15T12:00:00Z",
                        maxDurationSeconds = 86400,
                        heartbeat = new
                        {
                            intervalSeconds = 30,
                            retryIntervalSeconds = 15,
                            maxRetries = 5,
                            clientResponse = new { type = "pong" }
                        },
                        agentInstructions = new
                        {
                            requiredWorkflow = "For every ticket notification from this WebSocket or from the unread list, fetch links.ticket (or agentInstructions.ticketDetailPath), inspect the ticket, and handle/process/deal with it as best you can.",
                            recoveryWorkflow = "After reconnecting, call GET /api/notifications?unreadOnly=true and process any unread ticket notifications the same way.",
                            completionWorkflow = "Once the ticket has been handled, resolved, or documented with a blocker comment, call PATCH agentInstructions.markNotificationReadPath so the notification is consumed.",
                            unableToResolveAction = "If you cannot resolve or safely progress a ticket, POST a comment to /api/bugs/{id}/comments with your findings and blocker instead of silently dropping the notification.",
                            safetyNote = "Adding a comment is the low-risk AI fallback because it does not change ticket state and does not overwrite any report or resolution data."
                        },
                        serverTime = "2026-07-14T12:00:00Z"
                    },
                    heartbeat = new
                    {
                        serverPing = new { type = "ping", attempt = 0, maxRetries = 5, serverTime = "2026-07-14T12:10:00Z" },
                        agentPong = new { type = "pong" }
                    },
                    eventPayload = new
                    {
                        type = "ticket.assigned",
                        eventId = "stable_event_id",
                        ticketVersion = 4,
                        actionRequired = true,
                        notification = new
                        {
                            id = "notification_id",
                            userId = "usr_agent_001",
                            ticketId = "ticket_id",
                            kind = "ticket_assigned",
                            message = "Ticket ticket_id was assigned to you.",
                            isRead = false,
                            createdAt = "2026-07-14 12:00:00",
                            agentInstructions = new
                            {
                                actionRequired = true,
                                requiredWorkflow = "Fetch the ticket through the API, inspect the full details/activity, and handle/process/deal with it as best you can. Do not receive and ignore this notification.",
                                ticketDetailPath = "/api/bugs/ticket_id",
                                commentPath = "/api/bugs/ticket_id/comments",
                                markNotificationReadPath = "/api/notifications/notification_id/read",
                                completionAction = "After you have handled the ticket, resolved it, or left a blocker comment, mark this notification read so it is consumed and will not remain in the unread work queue.",
                                unableToResolveAction = "If you cannot resolve or safely progress the ticket, leave a comment explaining what you checked, what blocked you, and what a human should do next.",
                                safetyNote = "Comments are the low-risk fallback for AI agents: they do not change ticket state and do not overwrite ticket data."
                            }
                        },
                        links = new { ticket = "/api/bugs/ticket_id" },
                        agentInstructions = new
                        {
                            actionRequired = true,
                            requiredWorkflow = "Fetch the ticket through the API, inspect the full details/activity, and handle/process/deal with it as best you can. Do not receive and ignore this notification.",
                            ticketDetailPath = "/api/bugs/ticket_id",
                            commentPath = "/api/bugs/ticket_id/comments",
                            markNotificationReadPath = "/api/notifications/notification_id/read",
                            completionAction = "After you have handled the ticket, resolved it, or left a blocker comment, mark this notification read so it is consumed and will not remain in the unread work queue.",
                            unableToResolveAction = "If you cannot resolve or safely progress the ticket, leave a comment explaining what you checked, what blocked you, and what a human should do next.",
                            safetyNote = "Comments are the low-risk fallback for AI agents: they do not change ticket state and do not overwrite ticket data."
                        },
                        serverTime = "2026-07-14T12:00:01Z",
                        clientRule = "Deduplicate eventId. Ignore this event if ticketVersion is older than the newest version already observed; refetch before mutating."
                    },
                    commentEventPayload = new
                    {
                        type = "ticket.commented",
                        eventId = "stable_comment_event_id",
                        ticketVersion = 4,
                        actionRequired = true,
                        notification = new
                        {
                            id = "notification_id",
                            userId = "usr_agent_001",
                            ticketId = "ticket_id",
                            kind = "ticket_commented",
                            message = "Ticket ticket_id has a new comment.",
                            isRead = false,
                            createdAt = "2026-07-14 12:05:00",
                            agentInstructions = new
                            {
                                actionRequired = true,
                                requiredWorkflow = "Fetch the ticket through the API, inspect the full details/activity, and handle/process/deal with it as best you can. Do not receive and ignore this notification.",
                                ticketDetailPath = "/api/bugs/ticket_id",
                                commentPath = "/api/bugs/ticket_id/comments",
                                markNotificationReadPath = "/api/notifications/notification_id/read",
                                completionAction = "After you have handled the ticket, resolved it, or left a blocker comment, mark this notification read so it is consumed and will not remain in the unread work queue.",
                                unableToResolveAction = "If you cannot resolve or safely progress the ticket, leave a comment explaining what you checked, what blocked you, and what a human should do next.",
                                safetyNote = "Comments are the low-risk fallback for AI agents: they do not change ticket state and do not overwrite ticket data."
                            }
                        },
                        links = new { ticket = "/api/bugs/ticket_id" },
                        agentInstructions = new
                        {
                            actionRequired = true,
                            requiredWorkflow = "Fetch the ticket through the API, inspect the full details/activity, and handle/process/deal with it as best you can. Do not receive and ignore this notification.",
                            ticketDetailPath = "/api/bugs/ticket_id",
                            commentPath = "/api/bugs/ticket_id/comments",
                            markNotificationReadPath = "/api/notifications/notification_id/read",
                            completionAction = "After you have handled the ticket, resolved it, or left a blocker comment, mark this notification read so it is consumed and will not remain in the unread work queue.",
                            unableToResolveAction = "If you cannot resolve or safely progress the ticket, leave a comment explaining what you checked, what blocked you, and what a human should do next.",
                            safetyNote = "Comments are the low-risk fallback for AI agents: they do not change ticket state and do not overwrite ticket data."
                        },
                        serverTime = "2026-07-14T12:05:01Z"
                    }
                },
                new
                {
                    name = "Export selected tickets",
                    curl = "curl -OJ -X POST $BASE/api/bugs/export -H 'Authorization: Bearer <SENIOR_OR_ADMIN_TOKEN>' -H 'Content-Type: application/json' -d '{\"format\":\"csv\",\"ticketIds\":[\"<TICKET_ID>\"]}'"
                }
            }
        };

        return Results.Ok(examples);
    }
}
