using BugTracker.Api.Database;
using System.Collections.Concurrent;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeenWrites = new(StringComparer.Ordinal);

    public AuthRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }
}
