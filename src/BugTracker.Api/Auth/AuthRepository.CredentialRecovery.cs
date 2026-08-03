using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    public async Task<IReadOnlyList<UserRequestRecord>> ListCredentialRecoveryRequestsAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        const string sql = """
            SELECT r.recovery_id, r.request_type, r.email, u.username, r.status, r.user_id, r.created_at, r.updated_at
            FROM credential_recovery_requests r
            INNER JOIN users u ON u.user_id = r.user_id
            ORDER BY r.created_at DESC;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var requests = new List<UserRequestRecord>();
        while (await reader.ReadAsync(ct))
        {
            requests.Add(new UserRequestRecord(
                $"recovery_{reader.GetString(0)}", reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), null, null, reader.GetString(6), reader.GetString(7), "credential_recovery"));
        }

        return requests;
    }

    public async Task<bool> CreateCredentialRecoveryRequestAsync(string email, string requestType, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        const string supersedeSql = """
            UPDATE credential_recovery_requests
            SET status = 'superseded', updated_at = $now
            WHERE user_id = (SELECT user_id FROM users WHERE email = $email AND user_type = $request_type AND is_active = 1 LIMIT 1)
              AND request_type = $request_type
              AND status IN ('pending', 'issued');
            """;
        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = transaction;
            supersede.CommandText = supersedeSql;
            supersede.Parameters.AddWithValue("$now", nowText);
            supersede.Parameters.AddWithValue("$email", email);
            supersede.Parameters.AddWithValue("$request_type", requestType == "ai_agent" ? "agent" : "human");
            await supersede.ExecuteNonQueryAsync(ct);
        }

        const string insertSql = """
            INSERT INTO credential_recovery_requests (recovery_id, request_type, email, user_id, status, created_at, updated_at)
            SELECT $recovery_id, $request_type, u.email, u.user_id, 'pending', $now, $now
            FROM users u
            WHERE u.email = $email
              AND u.user_type = $user_type
              AND u.is_active = 1;
            """;
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = insertSql;
        insert.Parameters.AddWithValue("$recovery_id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$request_type", requestType);
        insert.Parameters.AddWithValue("$user_type", requestType == "ai_agent" ? "agent" : "human");
        insert.Parameters.AddWithValue("$email", email);
        insert.Parameters.AddWithValue("$now", nowText);
        var created = await insert.ExecuteNonQueryAsync(ct) > 0;
        await transaction.CommitAsync(ct);
        return created;
    }

    public async Task<CredentialRecoveryRecord?> GetCredentialRecoveryRequestAsync(string recoveryId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        const string sql = """
            SELECT r.recovery_id, r.request_type, r.email, r.user_id, u.username, r.status, r.token_hash, r.token_expires_at, r.created_at, r.updated_at
            FROM credential_recovery_requests r
            INNER JOIN users u ON u.user_id = r.user_id
            WHERE r.recovery_id = $recovery_id
            LIMIT 1;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$recovery_id", recoveryId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapCredentialRecovery(reader) : null;
    }

    public async Task<bool> IssuePasswordResetAsync(string recoveryId, string tokenHash, DateTimeOffset expiresAt, string issuerUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        const string supersedeSql = """
            UPDATE credential_recovery_requests
            SET status = 'superseded', token_hash = NULL, token_expires_at = NULL, updated_at = $now
            WHERE user_id = (SELECT user_id FROM credential_recovery_requests WHERE recovery_id = $recovery_id)
              AND request_type = 'human'
              AND status IN ('pending', 'issued');
            """;
        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = transaction;
            supersede.CommandText = supersedeSql;
            supersede.Parameters.AddWithValue("$now", nowText);
            supersede.Parameters.AddWithValue("$recovery_id", recoveryId);
            await supersede.ExecuteNonQueryAsync(ct);
        }

        const string issueSql = """
            UPDATE credential_recovery_requests
            SET status = 'issued', token_hash = $token_hash, token_expires_at = $expires_at, issued_by_user_id = $issuer_user_id, updated_at = $now
            WHERE recovery_id = $recovery_id
              AND request_type = 'human';
            """;
        await using var issue = connection.CreateCommand();
        issue.Transaction = transaction;
        issue.CommandText = issueSql;
        issue.Parameters.AddWithValue("$token_hash", tokenHash);
        issue.Parameters.AddWithValue("$expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        issue.Parameters.AddWithValue("$issuer_user_id", issuerUserId);
        issue.Parameters.AddWithValue("$now", nowText);
        issue.Parameters.AddWithValue("$recovery_id", recoveryId);
        var issued = await issue.ExecuteNonQueryAsync(ct) > 0;
        await transaction.CommitAsync(ct);
        return issued;
    }

    public async Task<bool> IssueAgentOathTokenRecoveryAsync(string recoveryId, string tokenHash, DateTimeOffset expiresAt, string issuerUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        const string sql = """
            UPDATE credential_recovery_requests
            SET status = 'issued', token_hash = $token_hash, token_expires_at = $expires_at, issued_by_user_id = $issuer_user_id, updated_at = $now
            WHERE recovery_id = $recovery_id
              AND request_type = 'ai_agent'
              AND status = 'pending';
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token_hash", tokenHash);
        command.Parameters.AddWithValue("$expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$issuer_user_id", issuerUserId);
        command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$recovery_id", recoveryId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> ConsumePasswordResetAsync(string email, string tokenHash, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        const string consumeSql = """
            UPDATE credential_recovery_requests
            SET status = 'used', token_hash = NULL, token_expires_at = NULL, updated_at = $now
            WHERE recovery_id = (
                SELECT recovery_id FROM credential_recovery_requests
                WHERE request_type = 'human' AND email = $email AND token_hash = $token_hash
                  AND status = 'issued' AND token_expires_at >= $now
                LIMIT 1)
            RETURNING user_id;
            """;
        string? userId;
        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = consumeSql;
            consume.Parameters.AddWithValue("$email", email);
            consume.Parameters.AddWithValue("$token_hash", tokenHash);
            consume.Parameters.AddWithValue("$now", nowText);
            userId = Convert.ToString(await consume.ExecuteScalarAsync(ct));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        const string passwordSql = "UPDATE users SET password_hash = $password_hash, updated_at = $now WHERE user_id = $user_id AND user_type = 'human' AND is_active = 1;";
        await using var password = connection.CreateCommand();
        password.Transaction = transaction;
        password.CommandText = passwordSql;
        password.Parameters.AddWithValue("$password_hash", passwordHash);
        password.Parameters.AddWithValue("$now", nowText);
        password.Parameters.AddWithValue("$user_id", userId);
        if (await password.ExecuteNonQueryAsync(ct) != 1)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    private static CredentialRecoveryRecord MapCredentialRecovery(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8), reader.GetString(9));
}
