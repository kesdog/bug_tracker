using BugTracker.Api.Database;
using System.Globalization;

namespace BugTracker.Api.Auth;

public sealed record LoginSecurityDecision(bool IsLocked, DateTimeOffset? LockedUntil);

public sealed class LoginSecurityMonitor(
    SqliteConnectionFactory connectionFactory,
    TokenService tokenService,
    ILogger<LoginSecurityMonitor> logger)
{
    // Keep both login flows at 20 failures and a 15-minute lockout for now. Move these
    // policy values to configuration when deployments need independently tunable limits.
    private const int MaximumFailedAttempts = 20;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginSecurityDecision> CheckAsync(string account, string publicIp, string flow, CancellationToken ct)
    {
        var subject = Fingerprint(account, publicIp);
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT locked_until
            FROM login_security_state
            WHERE account_fingerprint = $account AND ip_fingerprint = $ip AND flow = $flow;
            """;
        command.Parameters.AddWithValue("$account", subject.Account);
        command.Parameters.AddWithValue("$ip", subject.Ip);
        command.Parameters.AddWithValue("$flow", flow);
        var value = Convert.ToString(await command.ExecuteScalarAsync(ct));
        if (!TryParseUtc(value, out var lockedUntil) || lockedUntil <= DateTimeOffset.UtcNow)
        {
            return new(false, null);
        }

        return new(true, lockedUntil);
    }

    public async Task<LoginSecurityDecision> RecordFailureAsync(string account, string publicIp, string flow, CancellationToken ct)
    {
        var subject = Fingerprint(account, publicIp);
        var now = DateTimeOffset.UtcNow;
        var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var windowStartText = now.Subtract(LockoutDuration).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var lockedUntilText = now.Add(LockoutDuration).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO login_security_state (
                account_fingerprint, ip_fingerprint, flow, failed_attempts,
                first_failed_at, last_failed_at, locked_until)
            VALUES ($account, $ip, $flow, 1, $now, $now, NULL)
            ON CONFLICT(account_fingerprint, ip_fingerprint, flow) DO UPDATE SET
                failed_attempts = CASE
                    WHEN login_security_state.locked_until IS NOT NULL
                         AND login_security_state.locked_until <= $now THEN 1
                    WHEN login_security_state.first_failed_at <= $window_start THEN 1
                    ELSE login_security_state.failed_attempts + 1
                END,
                first_failed_at = CASE
                    WHEN login_security_state.locked_until IS NOT NULL
                         AND login_security_state.locked_until <= $now THEN $now
                    WHEN login_security_state.first_failed_at <= $window_start THEN $now
                    ELSE login_security_state.first_failed_at
                END,
                last_failed_at = $now,
                locked_until = CASE
                    WHEN login_security_state.locked_until IS NOT NULL
                         AND login_security_state.locked_until > $now THEN login_security_state.locked_until
                    ELSE NULL
                END;
            """;
        command.Parameters.AddWithValue("$account", subject.Account);
        command.Parameters.AddWithValue("$ip", subject.Ip);
        command.Parameters.AddWithValue("$flow", flow);
        command.Parameters.AddWithValue("$now", nowText);
        command.Parameters.AddWithValue("$window_start", windowStartText);
        await command.ExecuteNonQueryAsync(ct);

        await using var lockCommand = connection.CreateCommand();
        lockCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        lockCommand.CommandText = """
            UPDATE login_security_state
            SET locked_until = $locked_until
            WHERE account_fingerprint = $account AND ip_fingerprint = $ip AND flow = $flow
              AND failed_attempts >= $maximum AND locked_until IS NULL;
            """;
        lockCommand.Parameters.AddWithValue("$account", subject.Account);
        lockCommand.Parameters.AddWithValue("$ip", subject.Ip);
        lockCommand.Parameters.AddWithValue("$flow", flow);
        lockCommand.Parameters.AddWithValue("$maximum", MaximumFailedAttempts);
        lockCommand.Parameters.AddWithValue("$locked_until", lockedUntilText);
        var lockActivated = await lockCommand.ExecuteNonQueryAsync(ct) > 0;

        await using var readCommand = connection.CreateCommand();
        readCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        readCommand.CommandText = """
            SELECT locked_until
            FROM login_security_state
            WHERE account_fingerprint = $account AND ip_fingerprint = $ip AND flow = $flow;
            """;
        readCommand.Parameters.AddWithValue("$account", subject.Account);
        readCommand.Parameters.AddWithValue("$ip", subject.Ip);
        readCommand.Parameters.AddWithValue("$flow", flow);
        var value = Convert.ToString(await readCommand.ExecuteScalarAsync(ct));
        await transaction.CommitAsync(ct);
        var hasStoredLock = TryParseUtc(value, out var lockedUntil) && lockedUntil > now;
        var isLocked = lockActivated || hasStoredLock;
        if (isLocked)
        {
            logger.LogWarning(
                "Login security lockout activated for flow {Flow}, account fingerprint {AccountFingerprint}, and IP fingerprint {IpFingerprint}.",
                flow, subject.Account[..12], subject.Ip[..12]);
        }

        return new(isLocked, isLocked ? (hasStoredLock ? lockedUntil : now.Add(LockoutDuration)) : null);
    }

    public async Task RecordSuccessAsync(string account, string publicIp, string flow, CancellationToken ct)
    {
        var subject = Fingerprint(account, publicIp);
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM login_security_state
            WHERE account_fingerprint = $account AND ip_fingerprint = $ip AND flow = $flow;
            DELETE FROM login_security_state WHERE last_failed_at <= datetime('now', '-7 days');
            """;
        command.Parameters.AddWithValue("$account", subject.Account);
        command.Parameters.AddWithValue("$ip", subject.Ip);
        command.Parameters.AddWithValue("$flow", flow);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static IResult LockedResult(HttpContext context, DateTimeOffset? lockedUntil)
    {
        var retryAfter = lockedUntil is null
            ? (int)LockoutDuration.TotalSeconds
            : Math.Max(1, (int)Math.Ceiling((lockedUntil.Value - DateTimeOffset.UtcNow).TotalSeconds));
        context.Response.Headers.RetryAfter = retryAfter.ToString();
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(
            new { error = "too many failed login attempts", errorCode = "login_locked" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private (string Account, string Ip) Fingerprint(string account, string publicIp) =>
        (tokenService.HashToken(account), tokenService.HashToken(publicIp));

    private static bool TryParseUtc(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
}
