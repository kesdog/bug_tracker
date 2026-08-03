using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BugTracker.Api.Auth;

public sealed partial class AuthRepository
{
    private static IReadOnlyList<string> ReadProjects(SqliteDataReader reader, int columnIndex)
    {
        if (reader.IsDBNull(columnIndex))
        {
            return [];
        }

        var raw = reader.GetString(columnIndex);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var projects = JsonSerializer.Deserialize<List<string>>(raw);
            return projects is null ? [] : projects;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct)
    {
        return await _connectionFactory.OpenConnectionAsync(readOnly, ct);
    }
}
