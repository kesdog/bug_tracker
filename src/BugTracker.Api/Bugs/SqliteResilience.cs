using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public enum BugDataAccessError
{
    BusyConcurrency,
    Unreachable
}

public sealed class BugDataAccessException : Exception
{
    public BugDataAccessException(BugDataAccessError error, string message, Exception? innerException = null, int attempts = 1)
        : base(message, innerException)
    {
        Error = error;
        Attempts = attempts;
    }

    public BugDataAccessError Error { get; }
    public int Attempts { get; }
}

public static class SqliteResilience
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteIoErr = 10;
    private const int SqliteCantOpen = 14;

    public static bool IsBusy(SqliteException ex)
    {
        return ex.SqliteErrorCode is SqliteBusy or SqliteLocked
               || ex.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("database is busy", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnreachable(SqliteException ex)
    {
        return ex.SqliteErrorCode is SqliteCantOpen or SqliteIoErr
               || ex.Message.Contains("unable to open database", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("disk i/o", StringComparison.OrdinalIgnoreCase);
    }

    public static TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelayMs = Math.Min(2400, 180 * (int)Math.Pow(2, Math.Max(0, attempt - 1)));
        var jitterMs = Random.Shared.Next(35, 180);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);
    }
}
