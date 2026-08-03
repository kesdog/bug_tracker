using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    public async Task<UserRecord?> GetUserByEmailAsync(string email, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, password_hash, role, user_type, is_active, projects_json
            FROM users
            WHERE email = $email
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$email", email);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadProjects(reader, 6));
    }

    public async Task CreateAuthTokenAsync(
        string userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = """
                DELETE FROM auth_tokens
                WHERE user_id = $user_id
                  AND (revoked_at IS NOT NULL OR expires_at <= $issued_at);

                DELETE FROM auth_tokens
                WHERE token_id IN (
                    SELECT token_id
                    FROM auth_tokens
                    WHERE user_id = $user_id
                    ORDER BY issued_at DESC, token_id DESC
                    LIMIT -1 OFFSET 99
                );
                """;
            prune.Parameters.AddWithValue("$user_id", userId);
            prune.Parameters.AddWithValue("$issued_at", issuedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            await prune.ExecuteNonQueryAsync(ct);
        }

        const string sql = """
            INSERT INTO auth_tokens (token_id, user_id, token_hash, issued_at, expires_at)
            VALUES ($token_id, $user_id, $token_hash, $issued_at, $expires_at);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$token_hash", tokenHash);
        command.Parameters.AddWithValue("$issued_at", issuedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<AuthenticatedUser?> GetAuthenticatedUserByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT u.user_id, u.email, u.role, COALESCE(u.user_type, 'human') AS user_type, u.is_active, t.revoked_at, t.expires_at
            FROM auth_tokens t
            INNER JOIN users u ON u.user_id = t.user_id
            WHERE t.token_hash = $token_hash
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token_hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var isActive = reader.GetInt32(4) == 1;
        if (!isActive)
        {
            return null;
        }

        if (!reader.IsDBNull(5))
        {
            return null;
        }

        var expiresAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(6)), DateTimeKind.Utc);
        if (expiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return new AuthenticatedUser(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            tokenHash,
            new DateTimeOffset(expiresAt));
    }

    public async Task<AuthTokenAuditRecord?> GetAuthTokenAuditRecordAsync(string tokenHash, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT u.user_id, COALESCE(u.user_type, 'human') AS user_type, u.is_active, t.revoked_at, t.expires_at
            FROM auth_tokens t
            INNER JOIN users u ON u.user_id = t.user_id
            WHERE t.token_hash = $token_hash
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token_hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var expiresAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(4)), DateTimeKind.Utc);
        return new AuthTokenAuditRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            new DateTimeOffset(expiresAt));
    }

    public async Task<AuthAuditUserRecord?> GetAuditUserByUserIdAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, COALESCE(user_type, 'human') AS user_type
            FROM users
            WHERE user_id = $user_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new AuthAuditUserRecord(reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task RevokeAuthTokenAsync(string tokenHash, DateTimeOffset revokedAt, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE auth_tokens
            SET revoked_at = $revoked_at
            WHERE token_hash = $token_hash
              AND revoked_at IS NULL;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token_hash", tokenHash);
        command.Parameters.AddWithValue("$revoked_at", revokedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateLastSeenAsync(string userId, DateTimeOffset seenAt, CancellationToken ct)
    {
        while (true)
        {
            if (!_lastSeenWrites.TryGetValue(userId, out var previous))
            {
                if (_lastSeenWrites.TryAdd(userId, seenAt)) break;
                continue;
            }

            if (seenAt - previous < TimeSpan.FromMinutes(2)) return;
            if (_lastSeenWrites.TryUpdate(userId, seenAt, previous)) break;
        }

        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE users
            SET last_seen_at = $last_seen_at
            WHERE user_id = $user_id
              AND (last_seen_at IS NULL OR last_seen_at <= $update_before);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$last_seen_at", seenAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$update_before", seenAt.AddMinutes(-2).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$user_id", userId);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<AgentLoginRecord?> GetAgentUserByOathTokenAsync(string username, string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, role, user_type, is_active, projects_json, token_expires_at
            FROM (
                SELECT u.user_id, u.email, u.role, u.user_type, u.is_active, u.projects_json, r.api_key_expires_at AS token_expires_at
            FROM user_requests r
            INNER JOIN users u ON u.user_id = r.user_id
            WHERE r.request_type = 'ai_agent'
              AND r.status = 'approved'
              AND r.username = $username
              AND r.api_key_hash = $api_key_hash
              AND r.api_key_expires_at IS NOT NULL
              AND r.api_key_expires_at >= $now
              AND u.is_active = 1
            UNION ALL
            SELECT u.user_id, u.email, u.role, u.user_type, u.is_active, u.projects_json, r.token_expires_at AS token_expires_at
            FROM credential_recovery_requests r
            INNER JOIN users u ON u.user_id = r.user_id
            WHERE r.request_type = 'ai_agent'
              AND r.status = 'issued'
              AND u.username = $username
              AND r.token_hash = $api_key_hash
              AND r.token_expires_at IS NOT NULL
              AND r.token_expires_at >= $now
              AND u.is_active = 1
            )
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$api_key_hash", tokenHash);
        command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var expiresAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(6)), DateTimeKind.Utc);
        return new AgentLoginRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            ReadProjects(reader, 5),
            new DateTimeOffset(expiresAt));
    }
    public async Task<UserProfile?> GetUserProfileByUserIdAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT user_id, email, role, COALESCE(user_type, 'human') AS user_type, projects_json, username
            FROM users
            WHERE user_id = $user_id
              AND is_active = 1
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

        return new UserProfile(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ReadProjects(reader, 4),
            reader.GetString(5));
    }

    public async Task<bool> UpdatePasswordHashAsync(string userId, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE users
            SET password_hash = $password_hash,
                updated_at = $updated_at
            WHERE user_id = $user_id
              AND is_active = 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$password_hash", passwordHash);
        command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$user_id", userId);

        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }
}
