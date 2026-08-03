using BugTracker.Api.Bugs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class SqliteResilienceTests
{
    [Fact]
    public void Case1_IsBusy_WhenLockedErrorCode_ReturnsTrue()
    {
        // Simulates concurrent writer collision in SQLite.
        var ex = new SqliteException("database is locked", 5);

        var isBusy = SqliteResilience.IsBusy(ex);

        Assert.True(isBusy);
    }

    [Fact]
    public void Case2_IsUnreachable_WhenCantOpen_ReturnsTrue()
    {
        // Simulates DB file/path access issue.
        var ex = new SqliteException("unable to open database file", 14);

        var isUnreachable = SqliteResilience.IsUnreachable(ex);

        Assert.True(isUnreachable);
    }

    [Fact]
    public void Case3_GetRetryDelay_WithIncreasingAttempts_GrowsUntilCapped()
    {
        // Retries should back off (with jitter) instead of hammering writes.
        var first = SqliteResilience.GetRetryDelay(1);
        var second = SqliteResilience.GetRetryDelay(2);
        var late = SqliteResilience.GetRetryDelay(8);

        Assert.True(first > TimeSpan.Zero);
        Assert.True(second > first || second == first);
        Assert.True(late <= TimeSpan.FromSeconds(3));
    }
}
