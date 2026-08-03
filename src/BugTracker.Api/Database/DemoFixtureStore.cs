using System.Text.Json;
using BugTracker.Api.Auth;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

internal static class DemoFixtureStore
{
    internal const int UserCount = 7;
    internal const int ProjectCount = 5;
    internal const int TicketCount = 60;

    private static readonly UserFixture[] Users =
    [
        new("usr_admin_001", "admin", "admin@example.com", "admin", "AdminPass123!"),
        new("usr_senior_001", "alex.senior", "alex.senior@example.com", "senior", "SeniorPass123!"),
        new("usr_senior_002", "morgan.senior", "morgan.senior@example.com", "senior", "SeniorPass123!"),
        new("usr_dev_001", "ava.dev", "ava.dev@example.com", "dev", "DevPass123!"),
        new("usr_dev_002", "noah.dev", "noah.dev@example.com", "dev", "DevPass123!"),
        new("usr_dev_003", "mia.dev", "mia.dev@example.com", "dev", "DevPass123!"),
        new("usr_dev_004", "liam.dev", "liam.dev@example.com", "dev", "DevPass123!")
    ];

    private static readonly ProjectFixture[] Projects =
    [
        new("project-bugtracker", "bugtracker", "normal", "usr_senior_001", ["usr_admin_001", "usr_senior_001", "usr_dev_001", "usr_dev_004"]),
        new("project-currency-metal", "currency & metal converter", "normal", "usr_senior_001", ["usr_admin_001", "usr_senior_001", "usr_dev_002", "usr_dev_004"]),
        new("project-website-personal", "website (personal)", "normal", "usr_senior_001", ["usr_admin_001", "usr_senior_001", "usr_dev_001", "usr_dev_002"]),
        new("project-reservation", "reservation system", "normal", "usr_senior_002", ["usr_admin_001", "usr_senior_002", "usr_dev_003", "usr_dev_004"]),
        new("project-socket-manager", "socket manager", "sensitive", "usr_admin_001", ["usr_admin_001", "usr_senior_002", "usr_dev_003"])
    ];

    private static readonly string[] Statuses =
        ["todo", "todo", "todo", "todo", "open", "open", "open", "open", "reopened", "closed", "closed", "closed"];

    private static readonly Dictionary<string, TicketFixture[]> TicketsByProject = new(StringComparer.Ordinal)
    {
        ["project-bugtracker"] = ParseTickets("bugtracker", """
            Ticket list loses filters after browser back|page_not_loading|mid|p2|front-end,navigation,filters|always
            Markdown preview drops numbered reproduction steps|form_submission|mid|p2|front-end,editor,markdown|always
            Large attachment crashes ticket detail view|crash|high|p1|front-end,attachments,stability|always
            Duplicate ticket created when submit is retried|api|high|p1|back-end,idempotency,tickets|frequent
            Allocated queue omits newly assigned ticket|database|high|p1|back-end,allocation,freshness|intermittent
            Archive sort treats close dates as text|database|mid|p2|back-end,archive,sorting|always
            Sensitive project name leaks in ticket summary|api|urgent|p0|back-end,authorization,privacy|always
            Closing modal discards unsaved solution warning|form_submission|mid|p2|front-end,modal,data-loss|always
            Reopened ticket keeps stale closed badge|api|high|p1|front-end,status,cache|intermittent
            Reporter cannot reopen own resolved ticket|api|high|p1|back-end,authorization,workflow|always
            Activity timeline duplicates assignment event|database|mid|p2|back-end,activity,idempotency|intermittent
            Tag edit accepts mutually exclusive layers|api|low|p3|back-end,validation,tags|always
            """),
        ["project-currency-metal"] = ParseTickets("currency and metal converter", """
            JPY conversion incorrectly applies decimal minor units|database|high|p1|back-end,currency,precision|always
            Gold ounce selector resets to grams|form_submission|mid|p2|front-end,metals,units|always
            Rate chart crashes on market holiday gap|crash|high|p1|front-end,charts,market-data|always
            Stale exchange rate served beyond cache TTL|api|urgent|p0|back-end,cache,rates|frequent
            Silver price rounds before currency conversion|database|high|p1|back-end,metals,rounding|always
            Unsupported currency produces empty result card|page_not_loading|mid|p2|front-end,currency,errors|always
            Concurrent rate imports violate unique quote key|database|urgent|p0|back-end,imports,concurrency|intermittent
            Swap currencies keeps previous formatted precision|form_submission|low|p3|front-end,currency,formatting|always
            Recovered provider still marked unavailable|api|high|p1|back-end,provider,resilience|intermittent
            Platinum conversion uses palladium symbol|api|high|p1|back-end,metals,mapping|always
            Historical CSV exports locale-formatted numbers|form_submission|mid|p2|back-end,export,csv|always
            Negative metal weight bypasses API validation|api|high|p1|back-end,validation,metals|always
            """),
        ["project-website-personal"] = ParseTickets("personal website", """
            Portfolio grid overlaps at tablet width|page_not_loading|mid|p2|front-end,responsive,portfolio|always
            Contact form reports success when email delivery fails|form_submission|high|p1|back-end,contact,email|always
            Theme toggle crashes with blocked local storage|crash|mid|p2|front-end,theme,accessibility|always
            Draft article appears in public sitemap|database|urgent|p0|back-end,seo,privacy|always
            Resume download serves previous cached file|api|high|p1|back-end,resume,cache|frequent
            Keyboard focus disappears in mobile menu|page_not_loading|mid|p2|front-end,accessibility,navigation|always
            Image optimizer returns 500 for animated WebP|api|high|p1|back-end,images,media|always
            Analytics consent choice resets each visit|database|low|p3|front-end,privacy,cookies|always
            Revalidated article still shows old reading time|api|mid|p2|back-end,content,cache|intermittent
            External project links omit safe rel attributes|page_not_loading|mid|p2|front-end,security,links|always
            Contact message stores untrimmed sender email|database|low|p3|back-end,contact,normalization|always
            Print stylesheet hides employment dates|page_not_loading|mid|p2|front-end,resume,print|always
            """),
        ["project-reservation"] = ParseTickets("reservation system", """
            Two guests can reserve the final room|database|urgent|p0|back-end,booking,concurrency|intermittent
            Date picker allows checkout before check-in|form_submission|high|p1|front-end,dates,validation|always
            Booking page crashes when rate plan expires|crash|high|p1|front-end,checkout,rates|always
            Cancellation deadline calculated in server timezone|api|urgent|p0|back-end,cancellation,timezone|always
            Guest count omitted from confirmation email|api|mid|p2|back-end,email,confirmation|always
            Search cache mixes accessible-room preference|database|high|p1|back-end,search,accessibility|frequent
            Promo code remains after changing property|form_submission|high|p1|front-end,pricing,promotions|always
            Reservation lookup reveals guest surname|api|urgent|p0|back-end,privacy,lookup|always
            Reopened cancellation retains refunded state action|database|high|p1|back-end,refunds,workflow|always
            Waitlist promotion ignores room capacity|database|high|p1|back-end,waitlist,occupancy|always
            Payment retry creates duplicate reservation|api|urgent|p0|back-end,payments,idempotency|intermittent
            Occupancy tax excluded from printable receipt|page_not_loading|mid|p2|front-end,receipt,tax|always
            """),
        ["project-socket-manager"] = ParseTickets("socket manager", """
            Revoked API key keeps existing socket connected|api|urgent|p0|back-end,security,authentication|always
            Connection table exposes unmasked client tokens|page_not_loading|urgent|p0|front-end,security,secrets|always
            Malformed binary frame crashes gateway worker|crash|urgent|p0|back-end,protocol,stability|always
            Audit export includes cross-tenant connection IDs|database|urgent|p0|back-end,privacy,audit|always
            Heartbeat timeout disconnects healthy mobile clients|api|high|p1|back-end,heartbeat,mobile|frequent
            Reconnect storm bypasses per-client rate limit|database|urgent|p0|back-end,rate-limit,resilience|always
            Namespace filter sends sibling channel events|api|high|p1|back-end,routing,isolation|always
            Drain mode UI reports zero active sockets early|page_not_loading|mid|p2|front-end,operations,draining|always
            Reopened protocol alert loses packet evidence|database|high|p1|back-end,audit,evidence|always
            TLS rotation leaves stale gateway listeners|api|urgent|p0|back-end,tls,certificates|always
            Backpressure counter underflows after disconnect|database|high|p1|back-end,backpressure,metrics|intermittent
            Connection search accepts unbounded wildcard|database|high|p1|back-end,performance,diagnostics|always
            """)
    };

    internal static async Task DeleteBusinessDataChildFirstAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await ExecuteAsync(connection, transaction, """
            DELETE FROM outbox_messages;
            DELETE FROM notifications;
            DELETE FROM audit_logs;
            DELETE FROM ticket_attachments;
            DELETE FROM ticket_activity;
            DELETE FROM project_access_requests;
            DELETE FROM auth_tokens;
            DELETE FROM bug_tickets;
            DELETE FROM project_allocations;
            DELETE FROM user_requests;
            DELETE FROM projects;
            DELETE FROM users;
            DELETE FROM sqlite_sequence
            WHERE name IN ('project_allocations', 'audit_logs');
            """, ct);
    }

    internal static async Task<int> ReadNextGenerationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT generation + 1 FROM demo_reset_state WHERE singleton_id = 1;";
        var value = await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("The singleton demo reset state is missing.");
        return Convert.ToInt32(value);
    }

    internal static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PasswordHasherService passwordHasher,
        int generation,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var createdAt = Format(now.AddDays(-27));
        var projectNamesByUser = Users.ToDictionary(
            user => user.Id,
            user => Projects.Where(project => project.Members.Contains(user.Id)).Select(project => project.Name).ToArray());
        var hashes = Users.Select(user => user.Password).Distinct(StringComparer.Ordinal)
            .ToDictionary(password => password, passwordHasher.Hash, StringComparer.Ordinal);

        foreach (var user in Users)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO users
                    (user_id, email, username, password_hash, role, user_type, projects_json, is_active, created_at, updated_at)
                VALUES ($id, $email, $username, $hash, $role, 'human', $projects, 1, $created, $created);
                """, ct,
                ("$id", user.Id), ("$email", user.Email), ("$username", user.Username),
                ("$hash", hashes[user.Password]), ("$role", user.Role),
                ("$projects", JsonSerializer.Serialize(projectNamesByUser[user.Id])), ("$created", createdAt));
        }

        foreach (var project in Projects)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO projects (project_id, name, visibility, owner_user_id, created_at, updated_at)
                VALUES ($id, $name, $visibility, $owner, $created, $created);
                """, ct, ("$id", project.Id), ("$name", project.Name), ("$visibility", project.Visibility),
                ("$owner", project.OwnerId), ("$created", createdAt));

            foreach (var member in project.Members)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO project_allocations (project_id, user_id, created_at)
                    VALUES ($project, $user, $created);
                    """, ct, ("$project", project.Id), ("$user", member), ("$created", createdAt));
            }
        }

        var generationKey = $"g{generation:D6}";
        for (var projectIndex = 0; projectIndex < Projects.Length; projectIndex++)
        {
            var project = Projects[projectIndex];
            var projectTickets = TicketsByProject[project.Id];
            for (var ticketIndex = 0; ticketIndex < Statuses.Length; ticketIndex++)
            {
                var ticket = projectTickets[ticketIndex];
                var number = ticketIndex + 1;
                var status = Statuses[ticketIndex];
                var ticketId = $"demo-{generationKey}-{project.Id[8..]}-t{number:D2}";
                var reporter = project.Members[(ticketIndex + 1) % project.Members.Length];
                var assignee = status == "todo" ? null : project.Members[(ticketIndex + 2) % project.Members.Length];
                var created = now.AddDays(-(27 - ticketIndex * 2)).AddMinutes(projectIndex * 3);
                var assigned = assignee is null ? (DateTimeOffset?)null : created.AddHours(1);
                var closed = status == "closed" ? created.AddHours(6) : (DateTimeOffset?)null;
                var updated = closed ?? (status == "reopened" ? created.AddHours(8) : assigned ?? created);

                await ExecuteAsync(connection, transaction, """
                    INSERT INTO bug_tickets (
                        id, issue_title, description, bug_type, reporter_user_id, project_id, assignee_user_id,
                        created_at, updated_at, status, severity, priority, tags_json, environment,
                        expected_behavior, actual_behavior, steps_to_reproduce, frequency, close_date,
                        resolved_by_user_id, assigned_at, resolution_notes, post_resolution_report, version)
                    VALUES (
                        $id, $title, $description, $bug_type, $reporter, $project, $assignee,
                        $created, $updated, $status, $severity, $priority, $tags, 'demo',
                        $expected, $actual, $steps, $frequency, $closed,
                        $resolver, $assigned, $resolution, $report, 1);
                    """, ct,
                    ("$id", ticketId), ("$title", ticket.Title), ("$description", ticket.Description),
                    ("$bug_type", ticket.BugType), ("$reporter", reporter), ("$project", project.Id),
                    ("$assignee", assignee), ("$created", Format(created)), ("$updated", Format(updated)),
                    ("$status", status), ("$severity", ticket.Severity), ("$priority", ticket.Priority),
                    ("$tags", JsonSerializer.Serialize(ticket.Tags)),
                    ("$expected", ticket.ExpectedBehavior), ("$actual", ticket.ActualBehavior),
                    ("$steps", ticket.StepsToReproduce), ("$frequency", ticket.Frequency),
                    ("$closed", closed is null ? null : Format(closed.Value)),
                    ("$resolver", status == "closed" ? assignee : null),
                    ("$assigned", assigned is null ? null : Format(assigned.Value)),
                    ("$resolution", status == "closed" ? $"Corrected the root cause of '{ticket.Title}' and added regression coverage." : null),
                    ("$report", status == "closed" ? $"Verified the fix against the reported steps. {ticket.ExpectedBehavior}" : null));

                var activitySequence = 1;
                await InsertActivityAsync(connection, transaction, generationKey, ticketId, activitySequence++, reporter,
                    "created", "Ticket created.", created, ct);
                if (assigned is not null)
                {
                    await InsertActivityAsync(connection, transaction, generationKey, ticketId, activitySequence++, assignee!,
                        "assigned", "Ticket assigned.", assigned.Value, ct);
                }
                if (status == "reopened")
                {
                    await InsertActivityAsync(connection, transaction, generationKey, ticketId, activitySequence, reporter,
                        "reopened", "Issue reproduced after verification and reopened.", updated, ct);
                }
                else if (closed is not null)
                {
                    await InsertActivityAsync(connection, transaction, generationKey, ticketId, activitySequence, assignee!,
                        "closed", "Demo fix implemented and verified.", closed.Value, ct);
                }
            }
        }
    }

    internal static Task UpdateResetStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int generation,
        DateTimeOffset resetAt,
        string environment,
        CancellationToken ct) =>
        ExecuteAsync(connection, transaction, """
            UPDATE demo_reset_state
            SET generation = $generation, last_reset_at = $reset_at, last_environment = $environment
            WHERE singleton_id = 1;
            """, ct, ("$generation", generation), ("$reset_at", Format(resetAt)), ("$environment", environment));

    internal static async Task ValidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int generation,
        CancellationToken ct)
    {
        var invalid = await ScalarAsync(connection, transaction, """
            SELECT
              (SELECT COUNT(*) FROM users) <> 7 OR
              (SELECT COUNT(*) FROM users WHERE is_active = 1 AND user_type = 'human') <> 7 OR
              (SELECT COUNT(*) FROM users WHERE user_type = 'agent') <> 0 OR
              (SELECT COUNT(*) FROM users WHERE role = 'admin') <> 1 OR
              (SELECT COUNT(*) FROM users WHERE role = 'senior') <> 2 OR
              (SELECT COUNT(*) FROM users WHERE role = 'dev') <> 4 OR
              (SELECT COUNT(*) FROM projects) <> 5 OR
              (SELECT COUNT(*) FROM bug_tickets) <> 60 OR
              (SELECT COUNT(*) FROM projects WHERE name = 'socket manager' AND visibility = 'sensitive' AND owner_user_id = 'usr_admin_001') <> 1 OR
              (SELECT COUNT(*) FROM bug_tickets WHERE assigned_at IS NOT NULL AND assigned_at < created_at) <> 0 OR
              (SELECT COUNT(*) FROM bug_tickets WHERE close_date IS NOT NULL AND (assigned_at IS NULL OR close_date < assigned_at OR updated_at < close_date)) <> 0 OR
              (SELECT COUNT(*) FROM bug_tickets WHERE status = 'todo' AND (assignee_user_id IS NOT NULL OR assigned_at IS NOT NULL OR close_date IS NOT NULL)) <> 0 OR
              (SELECT COUNT(*) FROM bug_tickets WHERE status = 'closed' AND (close_date IS NULL OR resolved_by_user_id IS NULL)) <> 0 OR
              (SELECT COUNT(*) FROM (
                  SELECT project_id FROM bug_tickets GROUP BY project_id
                  HAVING COUNT(*) <> 12
                     OR SUM(status = 'todo') <> 4
                     OR SUM(status = 'open') <> 4
                     OR SUM(status = 'reopened') <> 1
                     OR SUM(status = 'closed') <> 3
              )) <> 0 OR
              (SELECT COUNT(*) FROM users u, json_each(u.projects_json) j
                 WHERE NOT EXISTS (
                   SELECT 1 FROM project_allocations a JOIN projects p ON p.project_id = a.project_id
                   WHERE a.user_id = u.user_id AND p.name = j.value
                 )) <> 0 OR
              (SELECT COUNT(*) FROM project_allocations a JOIN projects p ON p.project_id = a.project_id
                 WHERE NOT EXISTS (SELECT 1 FROM json_each((SELECT projects_json FROM users WHERE user_id = a.user_id)) j WHERE j.value = p.name)) <> 0 OR
              (SELECT COUNT(*) FROM pragma_foreign_key_check) <> 0 OR
              (SELECT generation FROM demo_reset_state WHERE singleton_id = 1) <> $generation;
            """, ct, ("$generation", generation));

        if (invalid != 0)
        {
            throw new InvalidOperationException("Demo fixture validation failed; the reset transaction was not committed.");
        }
    }

    private static Task InsertActivityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationKey,
        string ticketId,
        int sequence,
        string actor,
        string kind,
        string body,
        DateTimeOffset created,
        CancellationToken ct) =>
        ExecuteAsync(connection, transaction, """
            INSERT INTO ticket_activity (activity_id, ticket_id, actor_user_id, actor_type, kind, body, created_at)
            VALUES ($id, $ticket, $actor, 'human', $kind, $body, $created);
            """, ct, ("$id", $"demo-activity-{generationKey}-{ticketId[(ticketId.IndexOf('-', 5) + 1)..]}-{sequence:D2}"),
            ("$ticket", ticketId), ("$actor", actor), ("$kind", kind), ("$body", body), ("$created", Format(created)));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    private static TicketFixture T(
        string title, string description, string bugType, string severity, string priority, string[] tags,
        string expectedBehavior, string actualBehavior, string stepsToReproduce, string frequency) =>
        new(title, description, bugType, severity, priority, tags, expectedBehavior, actualBehavior, stepsToReproduce, frequency);

    private static TicketFixture[] ParseTickets(string projectName, string data) => data
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split('|'))
        .Select(parts => T(
            parts[0],
            $"In the {projectName}, the reported behavior is: {char.ToLowerInvariant(parts[0][0])}{parts[0][1..]}. This disrupts the affected workflow and produces an incorrect user-visible result.",
            parts[1], parts[2], parts[3], parts[4].Split(','),
            $"The {projectName} should complete this workflow reliably and return the documented result without the reported defect.",
            $"In the {projectName}, {char.ToLowerInvariant(parts[0][0])}{parts[0][1..]}; the incorrect result remains visible until the workflow is restarted.",
            $"Open the {projectName} in the demo environment, exercise the affected {parts[4].Split(',')[1]} workflow, and verify that '{parts[0]}' occurs.",
            parts[5]))
        .ToArray();

    private sealed record UserFixture(string Id, string Username, string Email, string Role, string Password);
    private sealed record ProjectFixture(string Id, string Name, string Visibility, string OwnerId, string[] Members);
    private sealed record TicketFixture(
        string Title,
        string Description,
        string BugType,
        string Severity,
        string Priority,
        string[] Tags,
        string ExpectedBehavior,
        string ActualBehavior,
        string StepsToReproduce,
        string Frequency);
}
