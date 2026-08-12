using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

public sealed class SqliteConnectionFactory
{
    private readonly string _writeConnectionString;
    private readonly string _readConnectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        var fullDatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullDatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        DatabaseDirectoryPath = directory
            ?? throw new InvalidOperationException("The database path does not have a containing directory.");
        DatabasePath = fullDatabasePath;

        var baseBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = fullDatabasePath,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 10,
            ForeignKeys = true
        };

        _writeConnectionString = new SqliteConnectionStringBuilder(baseBuilder.ToString())
        {
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _readConnectionString = new SqliteConnectionStringBuilder(baseBuilder.ToString())
        {
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
    }

    public string DatabaseDirectoryPath { get; }
    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct = default)
    {
        var connection = new SqliteConnection(readOnly ? _readConnectionString : _writeConnectionString);
        try
        {
            await connection.OpenAsync(ct);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = readOnly
                    ? "PRAGMA foreign_keys=ON; PRAGMA query_only=1; PRAGMA busy_timeout=5000;"
                    : "PRAGMA foreign_keys=ON; PRAGMA query_only=0; PRAGMA secure_delete=ON; PRAGMA busy_timeout=10000;";
                await command.ExecuteNonQueryAsync(ct);
            }

            await using var assertion = connection.CreateCommand();
            assertion.CommandText = "PRAGMA foreign_keys;";
            var enabled = Convert.ToInt32(await assertion.ExecuteScalarAsync(ct));
            if (enabled != 1)
            {
                throw new InvalidOperationException("SQLite foreign key enforcement could not be enabled for a database connection.");
            }

            if (!readOnly)
            {
                await using var secureDeleteAssertion = connection.CreateCommand();
                secureDeleteAssertion.CommandText = "PRAGMA secure_delete;";
                var secureDeleteEnabled = Convert.ToInt32(await secureDeleteAssertion.ExecuteScalarAsync(ct));
                if (secureDeleteEnabled != 1)
                {
                    throw new InvalidOperationException("SQLite secure_delete could not be enabled for a writable database connection.");
                }
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
