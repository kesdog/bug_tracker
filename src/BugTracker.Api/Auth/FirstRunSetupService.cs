using BugTracker.Api.Database;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public sealed class FirstRunSetupService(SqliteConnectionFactory connectionFactory)
{
    public const int DefaultHumanTokenTtlMinutes = 8 * 60;
    public const int DefaultAgentOathTtlDays = 30;
    public const int MinHumanTokenTtlMinutes = 15;
    public const int MaxHumanTokenTtlMinutes = 24 * 60;
    public const int MinAgentOathTtlDays = 1;
    public const int MaxAgentOathTtlDays = 62;

    public async Task<FirstRunSetupRecord> GetAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT phase, root_admin_user_id, first_project_id, human_token_ttl_minutes, agent_oath_ttl_days
            FROM first_run_setup WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new FirstRunSetupRecord("not_bootstrapped", null, null, null, null);
        return new FirstRunSetupRecord(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4));
    }

    public async Task<bool> CompletePasswordChangeAsync(string userId, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE users SET password_hash = $password_hash, updated_at = $now
            WHERE user_id = $user_id AND user_type = 'human' AND role = 'admin' AND is_active = 1;

            UPDATE auth_tokens SET revoked_at = $now
            WHERE user_id = $user_id AND revoked_at IS NULL;

            UPDATE first_run_setup SET phase = 'project_required', updated_at = $now
            WHERE singleton_id = 1 AND phase = 'password_change_required' AND root_admin_user_id = $user_id;
            """;
        command.Parameters.AddWithValue("$password_hash", passwordHash);
        command.Parameters.AddWithValue("$now", nowText);
        command.Parameters.AddWithValue("$user_id", userId);
        await command.ExecuteNonQueryAsync(ct);

        await using var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = "SELECT changes();";
        var changed = Convert.ToInt32(await verify.ExecuteScalarAsync(ct)) == 1;
        if (changed) await transaction.CommitAsync(ct); else await transaction.RollbackAsync(ct);
        return changed;
    }

    public async Task<bool> MarkFirstProjectAsync(string userId, string projectId, DateTimeOffset now, CancellationToken ct)
    {
        return await UpdateAsync("""
            UPDATE first_run_setup SET phase = 'ttl_required', first_project_id = $project_id, updated_at = $now
            WHERE singleton_id = 1 AND phase = 'project_required' AND root_admin_user_id = $user_id;
            """, userId, projectId, null, null, now, ct);
    }

    public async Task<bool> CompleteAsync(string userId, int humanTokenTtlMinutes, int agentOathTtlDays, DateTimeOffset now, CancellationToken ct)
    {
        return await UpdateAsync("""
            UPDATE first_run_setup SET phase = 'complete', human_token_ttl_minutes = $human_ttl,
                agent_oath_ttl_days = $agent_ttl, updated_at = $now
            WHERE singleton_id = 1 AND phase = 'ttl_required' AND root_admin_user_id = $user_id;
            """, userId, null, humanTokenTtlMinutes, agentOathTtlDays, now, ct);
    }

    private async Task<bool> UpdateAsync(string sql, string userId, string? projectId, int? humanTtl, int? agentTtl, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        if (projectId is not null) command.Parameters.AddWithValue("$project_id", projectId);
        if (humanTtl is not null) command.Parameters.AddWithValue("$human_ttl", humanTtl.Value);
        if (agentTtl is not null) command.Parameters.AddWithValue("$agent_ttl", agentTtl.Value);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }
}

public sealed record FirstRunSetupRecord(string Phase, string? RootAdminUserId, string? FirstProjectId, int? HumanTokenTtlMinutes, int? AgentOathTtlDays)
{
    public bool IsComplete => Phase == "complete";
    public bool IsRootAdmin(string userId) => string.Equals(RootAdminUserId, userId, StringComparison.Ordinal);
}
