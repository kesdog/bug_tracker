using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Projects;

public sealed partial class ProjectRepository
{
    private static async Task<string?> GetProjectOwnerIdAsync(SqliteConnection connection, SqliteTransaction transaction, string projectId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(owner_user_id, '') FROM projects WHERE project_id = $project_id;";
        command.Parameters.AddWithValue("$project_id", projectId);
        return await command.ExecuteScalarAsync(ct) as string;
    }
    private static async Task<IReadOnlyList<string>> FilterActiveUsersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var placeholders = new List<string>(userIds.Count);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < userIds.Count; i++)
        {
            var parameterName = $"$user_id_{i}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, userIds[i]);
        }

        command.CommandText = $"""
            SELECT user_id
            FROM users
            WHERE is_active = 1
              AND user_id IN ({string.Join(", ", placeholders)})
            ORDER BY user_id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var valid = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            valid.Add(reader.GetString(0));
        }

        return valid;
    }

    private static async Task<IReadOnlyList<string>> ListAllocatedUserIdsForProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string projectId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT user_id
            FROM project_allocations
            WHERE project_id = $project_id;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var userIds = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            userIds.Add(reader.GetString(0));
        }

        return userIds;
    }

    private static string BuildProjectId(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var slug = NonSlugChars.Replace(normalized, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "project";
        }

        var limited = slug.Length <= 40 ? slug : slug[..40];
        return $"project-{limited}";
    }

    private static async Task SyncUsersProjectsJsonAsync(SqliteConnection connection, CancellationToken ct)
    {
        const string usersSql = """
            SELECT user_id
            FROM users
            ORDER BY user_id ASC;
            """;

        await using var usersCommand = connection.CreateCommand();
        usersCommand.CommandText = usersSql;

        await using var userReader = await usersCommand.ExecuteReaderAsync(ct);
        var userIds = new List<string>();
        while (await userReader.ReadAsync(ct))
        {
            userIds.Add(userReader.GetString(0));
        }

        const string projectsSql = """
            SELECT p.name
            FROM project_allocations pa
            INNER JOIN projects p ON p.project_id = pa.project_id
            WHERE pa.user_id = $user_id
            ORDER BY p.name ASC;
            """;

        const string updateSql = """
            UPDATE users
            SET projects_json = $projects_json
            WHERE user_id = $user_id;
            """;

        foreach (var userId in userIds)
        {
            var projectNames = new List<string>();

            await using (var projectsCommand = connection.CreateCommand())
            {
                projectsCommand.CommandText = projectsSql;
                projectsCommand.Parameters.AddWithValue("$user_id", userId);

                await using var projectsReader = await projectsCommand.ExecuteReaderAsync(ct);
                while (await projectsReader.ReadAsync(ct))
                {
                    projectNames.Add(projectsReader.GetString(0));
                }
            }

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = updateSql;
            updateCommand.Parameters.AddWithValue("$projects_json", JsonSerializer.Serialize(projectNames));
            updateCommand.Parameters.AddWithValue("$user_id", userId);
            await updateCommand.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct)
    {
        return await _connectionFactory.OpenConnectionAsync(readOnly, ct);
    }
}
