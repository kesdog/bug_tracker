using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    public async Task<IReadOnlyList<AssignableUserRecord>> ListAssignableUsersAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, username, email, role, COALESCE(user_type, 'human') AS user_type
            FROM users
            WHERE is_active = 1
            ORDER BY user_id ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var users = new List<AssignableUserRecord>();
        while (await reader.ReadAsync(ct))
        {
            users.Add(new AssignableUserRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return users;
    }

    public async Task<IReadOnlyList<UserRoleRecord>> ListUsersAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, username, role, COALESCE(user_type, 'human') AS user_type, is_active, projects_json, last_seen_at
            FROM users
            WHERE user_id <> 'system'
            ORDER BY user_id ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var users = new List<UserRoleRecord>();
        while (await reader.ReadAsync(ct))
        {
            users.Add(new UserRoleRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                ReadProjects(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return users;
    }

    public async Task<UserRoleRecord?> UpdateUserRoleAsync(string userId, string role, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string updateSql = """
            UPDATE users
            SET role = $role,
                updated_at = $updated_at
            WHERE user_id = $user_id
              AND is_active = 1;
            """;

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.CommandText = updateSql;
            updateCommand.Parameters.AddWithValue("$role", role);
            updateCommand.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCommand.Parameters.AddWithValue("$user_id", userId);

            var rows = await updateCommand.ExecuteNonQueryAsync(ct);
            if (rows <= 0)
            {
                return null;
            }
        }

        const string selectSql = """
            SELECT user_id, email, username, role, COALESCE(user_type, 'human') AS user_type, is_active, projects_json, last_seen_at
            FROM users
            WHERE user_id = $user_id
            LIMIT 1;
            """;

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = selectSql;
        selectCommand.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await selectCommand.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserRoleRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadProjects(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async Task<UserRoleRecord?> CreateHumanUserAsync(
        string userId,
        string email,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        var timestamp = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var username = UsernamePolicy.DefaultFromEmail(email);
        const string insertSql = """
            INSERT INTO users (user_id, email, username, password_hash, role, projects_json, is_active, created_at, updated_at)
            VALUES ($user_id, $email, $username, $password_hash, 'dev', '[]', 1, $created_at, $updated_at);
            """;

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = insertSql;
            insertCommand.Parameters.AddWithValue("$user_id", userId);
            insertCommand.Parameters.AddWithValue("$email", email);
            insertCommand.Parameters.AddWithValue("$username", username);
            insertCommand.Parameters.AddWithValue("$password_hash", passwordHash);
            insertCommand.Parameters.AddWithValue("$created_at", timestamp);
            insertCommand.Parameters.AddWithValue("$updated_at", timestamp);

            try
            {
                await insertCommand.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return null;
            }
        }

        return new UserRoleRecord(userId, email, username, "dev", "human", 1, [], timestamp);
    }
    public async Task<UserRoleRecord?> GetUserRoleByUserIdAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, username, role, COALESCE(user_type, 'human') AS user_type, is_active, projects_json, last_seen_at
            FROM users
            WHERE user_id = $user_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserRoleRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadProjects(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async Task<UserRoleRecord?> UpdateUsernameAsync(string userId, string username, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE users
                SET username = $username,
                    updated_at = $updated_at
                WHERE user_id = $user_id;

                UPDATE user_requests
                SET username = $username,
                    updated_at = $updated_at
                WHERE user_id = $user_id
                  AND request_type = 'ai_agent';
                """;
            command.Parameters.AddWithValue("$username", username);
            command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$user_id", userId);
            if (await command.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }
        }

        await using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = """
            SELECT user_id, email, username, role, COALESCE(user_type, 'human'), is_active, projects_json, last_seen_at
            FROM users
            WHERE user_id = $user_id
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("$user_id", userId);
        await using var reader = await selectCommand.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        var updated = new UserRoleRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadProjects(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
        await reader.DisposeAsync();
        await transaction.CommitAsync(ct);
        return updated;
    }
}
