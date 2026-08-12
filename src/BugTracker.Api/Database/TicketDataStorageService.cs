using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

public sealed class TicketDataStorageOptions
{
    public long MaxBytes { get; init; } = 5_000_000_000;
}

public sealed record TicketDataStorageSnapshot(
    long TotalBytes,
    long DatabaseBytes,
    long WalBytes,
    long ShmBytes,
    IReadOnlyDictionary<string, long> ObjectBytes);

public sealed class TicketDataStorageService(
    SqliteConnectionFactory connectionFactory,
    Microsoft.Extensions.Options.IOptions<TicketDataStorageOptions> configuredOptions,
    ILogger<TicketDataStorageService> logger)
{
    private readonly long _maxBytes = configuredOptions.Value.MaxBytes;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var pageSizeCommand = connection.CreateCommand();
        pageSizeCommand.CommandText = "PRAGMA page_size;";
        var pageSize = Convert.ToInt64(await pageSizeCommand.ExecuteScalarAsync(ct));
        var maxPages = Math.Max(1, _maxBytes / pageSize);
        await using var capCommand = connection.CreateCommand();
        capCommand.CommandText = $"PRAGMA max_page_count={maxPages};";
        await capCommand.ExecuteNonQueryAsync(ct);

        await LogSnapshotAsync("startup", ct);
    }

    public async Task<bool> CanGrowAsync(long incomingBytes, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(ct);
        if (snapshot.TotalBytes + Math.Max(0, incomingBytes) <= _maxBytes)
        {
            return true;
        }

        LogSnapshot("rejected", snapshot);
        return false;
    }

    public async Task LogSnapshotAsync(string reason, CancellationToken ct = default) =>
        LogSnapshot(reason, await GetSnapshotAsync(ct));

    public async Task<TicketDataStorageSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var databaseBytes = FileSize(connectionFactory.DatabasePath);
        var walBytes = FileSize(connectionFactory.DatabasePath + "-wal");
        var shmBytes = FileSize(connectionFactory.DatabasePath + "-shm");
        var objectBytes = new Dictionary<string, long>(StringComparer.Ordinal);

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, SUM(pgsize) FROM dbstat GROUP BY name ORDER BY SUM(pgsize) DESC, name;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                objectBytes[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        catch (SqliteException error)
        {
            logger.LogWarning(error, "SQLite dbstat was unavailable while measuring ticket data tables.");
            await AddLogicalTableSizesAsync(objectBytes, ct);
        }

        return new(databaseBytes + walBytes + shmBytes, databaseBytes, walBytes, shmBytes, objectBytes);
    }

    private async Task AddLogicalTableSizesAsync(Dictionary<string, long> objectBytes, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
        var tableNames = new List<string>();
        await using (var tables = connection.CreateCommand())
        {
            tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await tables.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) tableNames.Add(reader.GetString(0));
        }

        foreach (var tableName in tableNames)
        {
            var quotedTable = QuoteIdentifier(tableName);
            var columns = new List<string>();
            await using (var tableInfo = connection.CreateCommand())
            {
                tableInfo.CommandText = $"PRAGMA table_info({quotedTable});";
                await using var reader = await tableInfo.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            }

            if (columns.Count == 0)
            {
                objectBytes[tableName] = 0;
                continue;
            }

            var expression = string.Join(" + ", columns.Select(column =>
                $"COALESCE(length(CAST({QuoteIdentifier(column)} AS BLOB)), 0)"));
            await using var size = connection.CreateCommand();
            size.CommandText = $"SELECT COALESCE(SUM({expression}), 0) FROM {quotedTable};";
            objectBytes[tableName] = Convert.ToInt64(await size.ExecuteScalarAsync(ct));
        }
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private void LogSnapshot(string reason, TicketDataStorageSnapshot snapshot)
    {
        logger.LogInformation(
            "Ticket data storage snapshot ({Reason}): total={TotalBytes}, limit={MaxBytes}, database={DatabaseBytes}, wal={WalBytes}, shm={ShmBytes}.",
            reason, snapshot.TotalBytes, _maxBytes, snapshot.DatabaseBytes, snapshot.WalBytes, snapshot.ShmBytes);
        foreach (var item in snapshot.ObjectBytes)
        {
            logger.LogInformation("Ticket data SQLite object size: {ObjectName}={ObjectBytes} bytes.", item.Key, item.Value);
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
