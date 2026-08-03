using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using BugTracker.Api.Database;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    private const int MaxWriteAttempts = 5;
    private static readonly TimeSpan MaxWriteRetryWindow = TimeSpan.FromSeconds(10);
    private static readonly Regex NonSlugChars = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly TicketWriteAuthorizationService _writeAuthorization;

    public BugRepository(SqliteConnectionFactory connectionFactory, TicketWriteAuthorizationService writeAuthorization)
    {
        _connectionFactory = connectionFactory;
        _writeAuthorization = writeAuthorization;
    }
}
