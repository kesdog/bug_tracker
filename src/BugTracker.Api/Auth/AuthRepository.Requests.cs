using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    public async Task<IReadOnlyList<UserRequestRecord>> ListUserRequestsAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT request_id, request_type, email, username, status, user_id, api_key_prefix, api_key_expires_at, created_at, updated_at
            FROM user_requests
            WHERE status <> 'removed'
            ORDER BY created_at DESC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var requests = new List<UserRequestRecord>();
        while (await reader.ReadAsync(ct))
        {
            requests.Add(new UserRequestRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return requests;
    }

    public async Task<UserRequestRecord?> CreateUserRequestAsync(string email, string requestType, string username, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var requestId = $"req_{Guid.NewGuid():N}";

        const string sql = """
            INSERT INTO user_requests (
                request_id, request_type, email, username, status, user_id, setup_token_hash,
                setup_token_expires_at, api_key_hash, api_key_prefix, api_key_expires_at, created_at, updated_at)
            VALUES (
                $request_id, $request_type, $email, $username, 'pending', NULL, NULL,
                NULL, NULL, NULL, NULL, $created_at, $updated_at);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$request_id", requestId);
        command.Parameters.AddWithValue("$request_type", requestType);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$created_at", nowText);
        command.Parameters.AddWithValue("$updated_at", nowText);

        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null;
        }

        return new UserRequestRecord(requestId, requestType, email, username, "pending", null, null, null, nowText, nowText);
    }

    public async Task<UserRequestRecord?> GetUserRequestByIdAsync(string requestId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT request_id, request_type, email, username, status, user_id, api_key_prefix, api_key_expires_at, created_at, updated_at
            FROM user_requests
            WHERE request_id = $request_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$request_id", requestId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserRequestRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9));
    }

    public async Task<UserRequestRecord?> GetAgentRequestByUserIdAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT request_id, request_type, email, username, status, user_id, api_key_prefix, api_key_expires_at, created_at, updated_at
            FROM user_requests
            WHERE request_type = 'ai_agent'
              AND status = 'approved'
              AND user_id = $user_id
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

        return new UserRequestRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9));
    }

    public async Task<UserRequestRecord?> UpdateRequestUsernameAsync(string requestId, string username, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string updateSql = """
            UPDATE user_requests
            SET username = $username,
                updated_at = $updated_at
            WHERE request_id = $request_id
              AND status = 'pending';
            """;

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = updateSql;
        updateCommand.Parameters.AddWithValue("$username", username);
        updateCommand.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        updateCommand.Parameters.AddWithValue("$request_id", requestId);

        try
        {
            var rows = await updateCommand.ExecuteNonQueryAsync(ct);
            if (rows <= 0)
            {
                return null;
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null;
        }

        return await GetUserRequestByIdAsync(requestId, ct);
    }

    public async Task<bool> RemoveRequestAsync(string requestId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE user_requests
            SET status = 'removed',
                updated_at = $updated_at
            WHERE request_id = $request_id
              AND status <> 'removed';
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$request_id", requestId);
        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> SetRequestSetupTokenAsync(string requestId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE user_requests
            SET setup_token_hash = $setup_token_hash,
                setup_token_expires_at = $setup_token_expires_at,
                updated_at = $updated_at
            WHERE request_id = $request_id
              AND request_type = 'human'
              AND status = 'pending';
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$setup_token_hash", tokenHash);
        command.Parameters.AddWithValue("$setup_token_expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$request_id", requestId);
        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<UserRequestRecord?> GetHumanRequestByEmailAndTokenHashAsync(string email, string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT request_id, request_type, email, username, status, user_id, api_key_prefix, api_key_expires_at, created_at, updated_at
            FROM user_requests
            WHERE request_type = 'human'
              AND email = $email
              AND setup_token_hash = $setup_token_hash
              AND setup_token_expires_at IS NOT NULL
              AND setup_token_expires_at >= $now
              AND status IN ('pending', 'approved')
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$setup_token_hash", tokenHash);
        command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserRequestRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9));
    }
}
