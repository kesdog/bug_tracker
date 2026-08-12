using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using BugTracker.Api.Database;

namespace BugTracker.Api.Projects;

public sealed partial class ProjectRepository
{
    private static readonly Regex NonSlugChars = new("[^a-z0-9]+", RegexOptions.Compiled);
    private readonly SqliteConnectionFactory _connectionFactory;

    public ProjectRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(string userId, string role, string userType, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT p.project_id, p.name, p.visibility, p.created_at, p.updated_at,
                   p.owner_user_id, owner.username, owner.role
            FROM projects p
            LEFT JOIN users owner ON owner.user_id = p.owner_user_id
            WHERE ($user_type = 'human' AND $role = 'admin')
               OR ($user_type = 'human' AND $role = 'senior' AND p.visibility = 'normal')
               OR EXISTS (
                    SELECT 1
                    FROM project_allocations pa
                    INNER JOIN users u ON u.user_id = pa.user_id
                    WHERE pa.project_id = p.project_id
                      AND pa.user_id = $user_id
                      AND u.is_active = 1
               )
            ORDER BY p.name ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$user_type", userType);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var projects = new List<ProjectDto>();
        while (await reader.ReadAsync(ct))
        {
            projects.Add(MapProject(reader));
        }

        return projects;
    }

    public async Task<ProjectDto?> GetProjectByIdAsync(string projectId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT p.project_id, p.name, p.visibility, p.created_at, p.updated_at,
                   p.owner_user_id, owner.username, owner.role
            FROM projects p
            LEFT JOIN users owner ON owner.user_id = p.owner_user_id
            WHERE p.project_id = $project_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return MapProject(reader);
    }

    public async Task<ProjectDto?> CreateProjectAsync(string name, string visibility, string ownerUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var baseProjectId = BuildProjectId(name);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var projectId = attempt == 0 ? baseProjectId : $"{baseProjectId}-{Guid.NewGuid().ToString("N")[..6]}";

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            const string sql = """
                INSERT INTO projects (project_id, name, visibility, owner_user_id, created_at, updated_at)
                SELECT $project_id, $name, $visibility, user_id, $created_at, $updated_at
                FROM users
                WHERE user_id = $owner_user_id AND is_active = 1 AND user_type = 'human' AND role IN ('senior', 'admin');

                INSERT INTO project_allocations (project_id, user_id, created_at)
                SELECT $project_id, $owner_user_id, $created_at
                WHERE changes() = 1;
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$project_id", projectId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$visibility", visibility);
            command.Parameters.AddWithValue("$owner_user_id", ownerUserId);
            command.Parameters.AddWithValue("$created_at", nowText);
            command.Parameters.AddWithValue("$updated_at", nowText);

            try
            {
                var rows = await command.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return null;
                }
                await transaction.CommitAsync(ct);
                await SyncUsersProjectsJsonAsync(connection, ct);
                return await GetProjectByIdAsync(projectId, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains("projects.project_id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains("projects.name", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return null;
    }

    public async Task<ProjectDto?> UpdateProjectVisibilityAsync(string projectId, string visibility, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE projects
            SET visibility = $visibility,
                updated_at = $updated_at
            WHERE project_id = $project_id;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$visibility", visibility);
        command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$project_id", projectId);
        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows == 0 ? null : await GetProjectByIdAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<string>> ListAllocatedProjectIdsForUserAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT pa.project_id
            FROM project_allocations pa
            INNER JOIN users u ON u.user_id = pa.user_id
            WHERE pa.user_id = $user_id
              AND u.is_active = 1
            ORDER BY pa.project_id ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var projectIds = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            projectIds.Add(reader.GetString(0));
        }

        return projectIds;
    }

    public async Task<bool> IsUserAllocatedToProjectAsync(string userId, string projectId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT 1
            FROM project_allocations pa
            INNER JOIN users u ON u.user_id = pa.user_id
            WHERE pa.user_id = $user_id
              AND pa.project_id = $project_id
              AND u.is_active = 1
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$project_id", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<string?> GetActiveUserRoleAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT role
            FROM users
            WHERE user_id = $user_id
              AND is_active = 1
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task<bool> HasAssigneeOutsideProjectMembershipAsync(string projectId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT 1
            FROM bug_tickets b
            WHERE b.project_id = $project_id
              AND b.assignee_user_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM project_allocations pa
                  WHERE pa.project_id = b.project_id
                    AND pa.user_id = b.assignee_user_id
              )
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project_id", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<IReadOnlyList<ProjectAllocationDto>> ListProjectAllocationsAsync(string userId, string role, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT p.project_id, p.name, p.visibility, pa.user_id,
                   p.owner_user_id, owner.username, owner.role
            FROM projects p
            LEFT JOIN project_allocations pa ON pa.project_id = p.project_id
            LEFT JOIN users owner ON owner.user_id = p.owner_user_id
            WHERE $role = 'admin'
               OR ($role = 'senior' AND p.visibility = 'normal')
               OR EXISTS (
                    SELECT 1
                    FROM project_allocations actor_pa
                    WHERE actor_pa.project_id = p.project_id
                      AND actor_pa.user_id = $user_id
               )
            ORDER BY p.name ASC, pa.user_id ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$role", role);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new Dictionary<string, (string Name, string Visibility, List<string> Users, string? OwnerId, string? OwnerUsername, string? OwnerRole)>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var projectId = reader.GetString(0);
            var projectName = reader.GetString(1);
            var visibility = reader.GetString(2);
            if (!rows.TryGetValue(projectId, out var value))
            {
                value = (projectName, visibility, [], reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6));
                rows[projectId] = value;
            }

            if (!reader.IsDBNull(3))
            {
                value.Users.Add(reader.GetString(3));
            }

            rows[projectId] = value;
        }

        return rows
            .Select(kvp => new ProjectAllocationDto(kvp.Key, kvp.Value.Name, kvp.Value.Visibility, kvp.Value.Users, kvp.Value.OwnerId, kvp.Value.OwnerUsername, kvp.Value.OwnerRole))
            .OrderBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProjectAllocationUpdateResult> ReplaceProjectAllocationsAsync(string projectId, IReadOnlyList<string> userIds, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var ownerId = await GetProjectOwnerIdAsync(connection, transaction, projectId, ct);
        if (ownerId is null)
        {
            await transaction.RollbackAsync(ct);
            return ProjectAllocationUpdateResult.Failure("project not found");
        }

        var normalizedRequestedIds = userIds.Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(ownerId) && !normalizedRequestedIds.Contains(ownerId))
        {
            await transaction.RollbackAsync(ct);
            return ProjectAllocationUpdateResult.Failure("project owner allocation cannot be removed; transfer ownership first");
        }

        const string deleteSql = """
            DELETE FROM project_allocations
            WHERE project_id = $project_id;
            """;

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = deleteSql;
            deleteCommand.Parameters.AddWithValue("$project_id", projectId);
            await deleteCommand.ExecuteNonQueryAsync(ct);
        }

        if (userIds.Count > 0)
        {
            var normalizedUserIds = userIds
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var validUserIds = await FilterActiveUsersAsync(connection, transaction, normalizedUserIds, ct);
            if (validUserIds.Count != normalizedUserIds.Count)
            {
                await transaction.RollbackAsync(ct);
                return ProjectAllocationUpdateResult.Failure("invalid user allocation data");
            }

            const string insertSql = """
                INSERT INTO project_allocations (project_id, user_id, created_at)
                VALUES ($project_id, $user_id, $created_at);
                """;

            foreach (var userId in validUserIds)
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = insertSql;
                insertCommand.Parameters.AddWithValue("$project_id", projectId);
                insertCommand.Parameters.AddWithValue("$user_id", userId);
                insertCommand.Parameters.AddWithValue("$created_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                await insertCommand.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);

        await SyncUsersProjectsJsonAsync(connection, ct);
        return ProjectAllocationUpdateResult.Success();
    }

    public async Task<IReadOnlyList<ProjectUserDto>> ListAllocatableUsersAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, username, role, user_type
            FROM users
            WHERE is_active = 1
            ORDER BY user_type ASC, role ASC, user_id ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var users = new List<ProjectUserDto>();
        while (await reader.ReadAsync(ct))
        {
            users.Add(new ProjectUserDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return users;
    }

    private static ProjectDto MapProject(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7));
}
