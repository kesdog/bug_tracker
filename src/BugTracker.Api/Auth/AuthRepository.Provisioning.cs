using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    public async Task<UserRoleRecord?> UpsertHumanUserFromRequestAsync(string requestId, string username, string email, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        string userId;
        const string findByEmailSql = """
            SELECT user_id FROM users WHERE email = $email LIMIT 1;
            """;

        await using (var findCommand = connection.CreateCommand())
        {
            findCommand.Transaction = transaction;
            findCommand.CommandText = findByEmailSql;
            findCommand.Parameters.AddWithValue("$email", email);
            var existing = await findCommand.ExecuteScalarAsync(ct);
            userId = existing as string ?? username;
        }

        const string upsertSql = """
            INSERT INTO users (user_id, email, username, password_hash, role, user_type, projects_json, is_active, created_at, updated_at)
            VALUES ($user_id, $email, $username, $password_hash, 'dev', 'human', '[]', 1, $created_at, $updated_at)
            ON CONFLICT(email) DO UPDATE SET
                password_hash = excluded.password_hash,
                role = 'dev',
                user_type = 'human',
                is_active = 1,
                updated_at = excluded.updated_at;
            """;

        await using (var upsertCommand = connection.CreateCommand())
        {
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = upsertSql;
            upsertCommand.Parameters.AddWithValue("$user_id", userId);
            upsertCommand.Parameters.AddWithValue("$email", email);
            upsertCommand.Parameters.AddWithValue("$username", username);
            upsertCommand.Parameters.AddWithValue("$password_hash", passwordHash);
            upsertCommand.Parameters.AddWithValue("$created_at", nowText);
            upsertCommand.Parameters.AddWithValue("$updated_at", nowText);
            await upsertCommand.ExecuteNonQueryAsync(ct);
        }

        const string updateRequestSql = """
            UPDATE user_requests
            SET status = 'approved',
                user_id = $user_id,
                setup_token_hash = NULL,
                setup_token_expires_at = NULL,
                updated_at = $updated_at
            WHERE request_id = $request_id;
            """;

        await using (var requestCommand = connection.CreateCommand())
        {
            requestCommand.Transaction = transaction;
            requestCommand.CommandText = updateRequestSql;
            requestCommand.Parameters.AddWithValue("$user_id", userId);
            requestCommand.Parameters.AddWithValue("$updated_at", nowText);
            requestCommand.Parameters.AddWithValue("$request_id", requestId);
            await requestCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return await GetUserRoleByUserIdAsync(userId, ct);
    }

    public async Task<bool> SetAgentApiKeyAsync(string requestId, string apiKeyHash, string apiKeyPrefix, DateTimeOffset expiresAt, string userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        const string ensureUserSql = """
            INSERT INTO users (user_id, email, username, password_hash, role, user_type, projects_json, is_active, created_at, updated_at)
            SELECT $user_id, email, username, $password_hash, 'dev', 'agent', '[]', 1, $created_at, $updated_at
            FROM user_requests
            WHERE request_id = $request_id
            ON CONFLICT(user_id) DO UPDATE SET
                role = 'dev',
                user_type = 'agent',
                is_active = 1,
                updated_at = excluded.updated_at;
            """;

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = ensureUserSql;
            userCommand.Parameters.AddWithValue("$user_id", userId);
            userCommand.Parameters.AddWithValue("$password_hash", "agent-login-disabled");
            userCommand.Parameters.AddWithValue("$created_at", nowText);
            userCommand.Parameters.AddWithValue("$updated_at", nowText);
            userCommand.Parameters.AddWithValue("$request_id", requestId);
            var rows = await userCommand.ExecuteNonQueryAsync(ct);
            if (rows <= 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        const string updateRequestSql = """
            UPDATE user_requests
            SET status = 'approved',
                user_id = $user_id,
                api_key_hash = $api_key_hash,
                api_key_prefix = $api_key_prefix,
                api_key_expires_at = $api_key_expires_at,
                updated_at = $updated_at
            WHERE request_id = $request_id
              AND request_type = 'ai_agent';
            """;

        await using (var requestCommand = connection.CreateCommand())
        {
            requestCommand.Transaction = transaction;
            requestCommand.CommandText = updateRequestSql;
            requestCommand.Parameters.AddWithValue("$user_id", userId);
            requestCommand.Parameters.AddWithValue("$api_key_hash", apiKeyHash);
            requestCommand.Parameters.AddWithValue("$api_key_prefix", apiKeyPrefix);
            requestCommand.Parameters.AddWithValue("$api_key_expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            requestCommand.Parameters.AddWithValue("$updated_at", nowText);
            requestCommand.Parameters.AddWithValue("$request_id", requestId);
            var rows = await requestCommand.ExecuteNonQueryAsync(ct);
            if (rows <= 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}
