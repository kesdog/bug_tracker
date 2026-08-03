using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Projects;

public sealed partial class ProjectRepository
{
    public async Task<ProjectDto?> TransferOwnerAsync(string projectId, string ownerUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE projects
                SET owner_user_id = $owner, updated_at = $updated
                WHERE project_id = $project
                  AND EXISTS (
                      SELECT 1 FROM users
                      WHERE user_id = $owner AND is_active = 1 AND user_type = 'human'
                        AND (role = 'admin' OR (role = 'senior' AND projects.visibility = 'normal'))
                  );
                """;
            update.Parameters.AddWithValue("$owner", ownerUserId);
            update.Parameters.AddWithValue("$updated", nowText);
            update.Parameters.AddWithValue("$project", projectId);
            if (await update.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }
        }

        await using (var allocation = connection.CreateCommand())
        {
            allocation.Transaction = transaction;
            allocation.CommandText = "INSERT OR IGNORE INTO project_allocations (project_id, user_id, created_at) VALUES ($project, $owner, $created);";
            allocation.Parameters.AddWithValue("$project", projectId);
            allocation.Parameters.AddWithValue("$owner", ownerUserId);
            allocation.Parameters.AddWithValue("$created", nowText);
            await allocation.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        await SyncUsersProjectsJsonAsync(connection, ct);
        return await GetProjectByIdAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<SafeProjectContactDto>> ListSafeReviewContactsAsync(string projectId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.user_id, u.username, u.role,
                   CASE WHEN p.owner_user_id = u.user_id THEN 0 ELSE 1 END AS preference
            FROM users u
            CROSS JOIN projects p
            WHERE p.project_id = $project
              AND u.is_active = 1 AND u.user_type = 'human' AND u.role IN ('admin', 'senior')
              AND (u.user_id = p.owner_user_id OR u.role = 'admin' OR (p.visibility = 'normal' AND u.role = 'senior'))
            ORDER BY preference, CASE u.role WHEN 'admin' THEN 0 ELSE 1 END, u.username, u.user_id
            LIMIT 5;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var contacts = new List<SafeProjectContactDto>();
        while (await reader.ReadAsync(ct)) contacts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return contacts;
    }

    public async Task<ProjectAccessRequestDto?> CreateAccessRequestAsync(string projectId, string requesterUserId, string sourceTicketId, string? reason, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var requestId = Guid.NewGuid().ToString("N");
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT OR IGNORE INTO project_access_requests
                    (request_id, project_id, requester_user_id, source_ticket_id, reason, status, created_at, updated_at)
                VALUES ($id, $project, $requester, $ticket, $reason, 'pending', $created, $created);
                """;
            command.Parameters.AddWithValue("$id", requestId);
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$requester", requesterUserId);
            command.Parameters.AddWithValue("$ticket", sourceTicketId);
            command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", nowText);
            await command.ExecuteNonQueryAsync(ct);
        }

        await using var lookup = connection.CreateCommand();
        lookup.CommandText = AccessRequestSelect + " WHERE r.project_id = $project AND r.requester_user_id = $requester AND r.status = 'pending' LIMIT 1;";
        lookup.Parameters.AddWithValue("$project", projectId);
        lookup.Parameters.AddWithValue("$requester", requesterUserId);
        await using var reader = await lookup.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapAccessRequest(reader) : null;
    }

    public async Task<IReadOnlyList<ProjectAccessRequestDto>> ListAccessRequestsAsync(string actorUserId, string actorRole, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = AccessRequestSelect + """
            WHERE $role = 'admin'
               OR (p.visibility = 'normal' AND p.owner_user_id = $actor AND $role = 'senior')
            ORDER BY r.created_at ASC, r.request_id ASC;
            """;
        command.Parameters.AddWithValue("$role", actorRole);
        command.Parameters.AddWithValue("$actor", actorUserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<ProjectAccessRequestDto>();
        while (await reader.ReadAsync(ct)) results.Add(MapAccessRequest(reader));
        return results;
    }

    public async Task<ProjectAccessRequestDto?> ReviewAccessRequestAsync(string requestId, string reviewerUserId, string reviewerRole, string status, string? note, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        string? requester = null;
        string? project = null;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT r.requester_user_id, r.project_id
                FROM project_access_requests r JOIN projects p ON p.project_id = r.project_id
                JOIN users requester ON requester.user_id = r.requester_user_id AND requester.is_active = 1
                JOIN users reviewer ON reviewer.user_id = $reviewer
                    AND reviewer.is_active = 1 AND reviewer.user_type = 'human'
                WHERE r.request_id = $id AND r.status = 'pending'
                  AND (reviewer.role = 'admin' OR (reviewer.role = 'senior' AND p.visibility = 'normal' AND p.owner_user_id = reviewer.user_id));
                """;
            read.Parameters.AddWithValue("$id", requestId);
            read.Parameters.AddWithValue("$reviewer", reviewerUserId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) { requester = reader.GetString(0); project = reader.GetString(1); }
        }
        if (requester is null || project is null) { await transaction.RollbackAsync(ct); return null; }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE project_access_requests SET status=$status, reviewed_by_user_id=$reviewer, review_note=$note, reviewed_at=$now, updated_at=$now WHERE request_id=$id AND status='pending';";
            update.Parameters.AddWithValue("$status", status);
            update.Parameters.AddWithValue("$reviewer", reviewerUserId);
            update.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            update.Parameters.AddWithValue("$now", nowText);
            update.Parameters.AddWithValue("$id", requestId);
            if (await update.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }
        }
        if (status == "approved")
        {
            await using var allocation = connection.CreateCommand();
            allocation.Transaction = transaction;
            allocation.CommandText = "INSERT OR IGNORE INTO project_allocations (project_id,user_id,created_at) VALUES ($project,$user,$now);";
            allocation.Parameters.AddWithValue("$project", project);
            allocation.Parameters.AddWithValue("$user", requester);
            allocation.Parameters.AddWithValue("$now", nowText);
            await allocation.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        await SyncUsersProjectsJsonAsync(connection, ct);

        await using var lookup = connection.CreateCommand();
        lookup.CommandText = AccessRequestSelect + " WHERE r.request_id = $id LIMIT 1;";
        lookup.Parameters.AddWithValue("$id", requestId);
        await using var resultReader = await lookup.ExecuteReaderAsync(ct);
        return await resultReader.ReadAsync(ct) ? MapAccessRequest(resultReader) : null;
    }

    private const string AccessRequestSelect = """
        SELECT r.request_id, r.project_id, r.requester_user_id, u.username, u.role, u.user_type,
               r.source_ticket_id, r.reason, r.status, r.reviewed_by_user_id, r.review_note,
               r.created_at, r.updated_at, r.reviewed_at
        FROM project_access_requests r
        JOIN projects p ON p.project_id = r.project_id
        JOIN users u ON u.user_id = r.requester_user_id
        """;

    private static ProjectAccessRequestDto MapAccessRequest(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13));
}
