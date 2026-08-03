using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    public async Task<UserIdentityDto?> GetRelevantActiveContactAsync(string ticketId, string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.user_id, u.username, u.role, u.user_type, u.email
            FROM bug_tickets b
            JOIN projects p ON p.project_id = b.project_id
            JOIN users u ON u.user_id = $user
            WHERE b.id = $ticket AND u.is_active = 1
              AND u.user_id IN (b.reporter_user_id, b.assignee_user_id, b.resolved_by_user_id, p.owner_user_id)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$ticket", ticketId);
        command.Parameters.AddWithValue("$user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new UserIdentityDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }
    /// <summary>
    /// Fast existence check used before update operations.
    /// </summary>
    private static async Task<string?> GetBugProjectIdAsync(SqliteConnection connection, string bugId, CancellationToken ct)
    {
        const string sql = """
            SELECT project_id
            FROM bug_tickets
            WHERE id = $id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", bugId);
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar as string;
    }

    private static async Task<string?> GetActiveBugProjectIdAsync(SqliteConnection connection, string bugId, CancellationToken ct)
    {
        const string sql = """
            SELECT project_id
            FROM bug_tickets
            WHERE id = $id
              AND status IN ('todo', 'open', 'reopened')
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", bugId);
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar as string;
    }

    /// <summary>
    /// Verifies that assignee id exists and is active.
    /// </summary>
    private static async Task<string?> GetAssigneeUserTypeAsync(SqliteConnection connection, string assigneeUserId, CancellationToken ct)
    {
        const string sql = """
            SELECT user_type
            FROM users
            WHERE user_id = $user_id
              AND is_active = 1
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", assigneeUserId);
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar as string;
    }

    private static async Task<string?> GetProjectVisibilityAsync(SqliteConnection connection, string projectId, CancellationToken ct)
    {
        const string sql = """
            SELECT visibility
            FROM projects
            WHERE project_id = $project_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project_id", projectId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<bool> IsUserAllocatedToProjectAsync(
        SqliteConnection connection,
        string userId,
        string projectId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT 1
            FROM project_allocations
            WHERE user_id = $user_id
              AND project_id = $project_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$project_id", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<bool> HasHumanDeveloperOnProjectAsync(SqliteConnection connection, string projectId, CancellationToken ct)
    {
        const string sql = """
            SELECT 1
            FROM project_allocations pa
            INNER JOIN users u ON u.user_id = pa.user_id
            WHERE pa.project_id = $project_id
              AND u.is_active = 1
              AND u.role IN ('dev', 'senior')
              AND COALESCE(u.user_type, 'human') = 'human'
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project_id", projectId);
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar is not null;
    }

    /// <summary>
    /// Shared connection-level bug lookup used within transactional flows.
    /// </summary>
    private static async Task<BugTicketDto?> GetBugByIdAsync(SqliteConnection connection, string id, CancellationToken ct)
    {

        const string sql = """
            SELECT
                b.id,
                b.issue_title,
                b.description,
                b.bug_type,
                b.project_id,
                COALESCE(p.name, b.project_id) AS project_name,
                b.reporter_user_id,
                b.assignee_user_id,
                b.created_at,
                b.updated_at,
                b.status,
                b.severity,
                b.priority,
                b.tags_json,
                b.environment,
                b.expected_behavior,
                b.actual_behavior,
                b.steps_to_reproduce,
                b.frequency,
                b.close_date,
                b.resolved_by_user_id,
                b.assigned_at,
                b.resolution_notes,
                b.post_resolution_report,
                b.cancellation_reason,
                b.report_images_json,
                b.resolution_report_images_json,
                b.text_evidence_json,
                b.version
            FROM bug_tickets b
            LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE b.id = $id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);

        BugTicketDto ticket;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            ticket = MapBugTicket(reader, 25, 26, 27, 28, 24);
        }

        var attachments = await ListTicketAttachmentsAsync(connection, id, ct);
        var activity = await ListTicketActivityAsync(connection, id, ct);
        var identities = await LoadTicketIdentitiesAsync(connection, ticket, ct);
        return ticket with
        {
            Attachments = attachments,
            Activity = activity,
            Reporter = identities.Reporter,
            Assignee = identities.Assignee,
            Resolver = identities.Resolver,
            Contacts = identities.Contacts
        };
    }

    private static async Task<(UserIdentityDto? Reporter, UserIdentityDto? Assignee, UserIdentityDto? Resolver, IReadOnlyList<TicketContactDto> Contacts)> LoadTicketIdentitiesAsync(
        SqliteConnection connection, BugTicketDto ticket, CancellationToken ct)
    {
        const string sql = """
            SELECT u.user_id, u.username, u.role, u.user_type, u.email, source.kind
            FROM (
                SELECT reporter_user_id AS user_id, 'reporter' AS kind FROM bug_tickets WHERE id = $id
                UNION ALL SELECT assignee_user_id, 'assignee' FROM bug_tickets WHERE id = $id AND assignee_user_id IS NOT NULL
                UNION ALL SELECT resolved_by_user_id, 'resolver' FROM bug_tickets WHERE id = $id AND resolved_by_user_id IS NOT NULL
                UNION ALL SELECT p.owner_user_id, 'owner' FROM bug_tickets b JOIN projects p ON p.project_id = b.project_id WHERE b.id = $id AND p.owner_user_id IS NOT NULL
            ) source
            JOIN users u ON u.user_id = source.user_id
            ORDER BY source.kind, u.user_id;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", ticket.Id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var grouped = new Dictionary<string, (UserIdentityDto Identity, HashSet<string> Kinds)>(StringComparer.Ordinal);
        UserIdentityDto? reporter = null, assignee = null, resolver = null;
        while (await reader.ReadAsync(ct))
        {
            var identity = new UserIdentityDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
            var kind = reader.GetString(5);
            if (kind == "reporter") reporter = identity;
            else if (kind == "assignee") assignee = identity;
            else if (kind == "resolver") resolver = identity;
            if (!grouped.TryGetValue(identity.UserId, out var entry)) entry = (identity, []);
            entry.Kinds.Add(kind);
            grouped[identity.UserId] = entry;
        }
        var contacts = grouped.Values
            .Select(x => new TicketContactDto(x.Identity.UserId, x.Identity.Username, x.Identity.Role, x.Identity.UserType, x.Identity.Email, x.Kinds.Order().ToArray()))
            .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase).ToArray();
        return (reporter, assignee, resolver, contacts);
    }

}
