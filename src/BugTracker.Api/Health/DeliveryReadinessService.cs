using System.Globalization;
using System.Reflection;
using BugTracker.Api.Database;
using Microsoft.Extensions.Options;

namespace BugTracker.Api.Health;

public sealed record DeliveryReadinessResult(
    bool IsReady,
    string Status,
    string Database,
    string Migrations,
    string Maintenance,
    string Storage,
    string DatabaseDirectory,
    string FreeSpace,
    long MinimumFreeBytes,
    int ExpectedMigrationVersion,
    int? AppliedMigrationVersion);

public sealed class ReadinessOptions
{
    public const long DefaultMinimumFreeBytes = 100L * 1024 * 1024;

    public long MinimumFreeBytes { get; set; } = DefaultMinimumFreeBytes;
}

public sealed class DeliveryReadinessService(
    SqliteConnectionFactory connectionFactory,
    IResetMaintenanceState resetMaintenanceState,
    IOptions<ReadinessOptions> options,
    ILogger<DeliveryReadinessService> logger)
{
    private static readonly int[] EmbeddedMigrationVersions = ReadEmbeddedMigrationVersions();

    public async Task<DeliveryReadinessResult> CheckAsync(CancellationToken ct)
    {
        var expectedVersion = EmbeddedMigrationVersions[^1];
        var minimumFreeBytes = options.Value.MinimumFreeBytes;
        if (resetMaintenanceState.IsResetInProgress)
        {
            return new DeliveryReadinessResult(
                false, "not_ready", "not_checked", "not_checked", "reset_in_progress",
                "not_checked", "not_checked", "not_checked", minimumFreeBytes, expectedVersion, null);
        }

        var directoryStatus = "not_writable";
        var freeSpaceStatus = "unknown";
        var storageStatus = "unavailable";
        try
        {
            await VerifyDirectoryWritableAsync(connectionFactory.DatabaseDirectoryPath, ct);
            directoryStatus = "writable";

            var availableFreeBytes = GetAvailableFreeSpace(connectionFactory.DatabaseDirectoryPath);
            freeSpaceStatus = availableFreeBytes >= minimumFreeBytes ? "sufficient" : "insufficient";
            storageStatus = freeSpaceStatus == "sufficient" ? "available" : "insufficient_space";
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Readiness database storage check failed.");
        }

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
            await using (var accessCheck = connection.CreateCommand())
            {
                accessCheck.CommandText = "SELECT 1;";
                _ = await accessCheck.ExecuteScalarAsync(ct);
            }

            var appliedVersions = new List<int>();
            await using (var migrationCheck = connection.CreateCommand())
            {
                migrationCheck.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
                await using var reader = await migrationCheck.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    appliedVersions.Add(reader.GetInt32(0));
                }
            }

            var currentVersion = appliedVersions.Count == 0 ? 0 : appliedVersions[^1];
            var migrationsCurrent = appliedVersions.SequenceEqual(EmbeddedMigrationVersions);
            var isReady = migrationsCurrent && storageStatus == "available";
            if (isReady && resetMaintenanceState.IsResetInProgress)
            {
                return ResetInProgress(minimumFreeBytes, expectedVersion, currentVersion);
            }
            return new DeliveryReadinessResult(
                isReady,
                isReady ? "ready" : "not_ready",
                "available",
                migrationsCurrent ? "current" : "out_of_date",
                "inactive",
                storageStatus,
                directoryStatus,
                freeSpaceStatus,
                minimumFreeBytes,
                expectedVersion,
                currentVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Readiness database check failed.");
            return new DeliveryReadinessResult(
                false, "not_ready", "unavailable", "unknown", "inactive",
                storageStatus, directoryStatus, freeSpaceStatus, minimumFreeBytes, expectedVersion, null);
        }
    }

    public static DeliveryReadinessResult ResetInProgress(long minimumFreeBytes, int expectedVersion, int? appliedVersion = null) =>
        new(false, "not_ready", "not_checked", "not_checked", "reset_in_progress",
            "not_checked", "not_checked", "not_checked", minimumFreeBytes, expectedVersion, appliedVersion);

    private static async Task VerifyDirectoryWritableAsync(string directoryPath, CancellationToken ct)
    {
        var probePath = Path.Combine(directoryPath, $".bug-tracker-readiness-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await probe.WriteAsync(new byte[] { 0 }, ct);
            await probe.FlushAsync(ct);
        }
        finally
        {
            // DeleteOnClose handles the normal path; this also cleans up if opening or writing fails.
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static long GetAvailableFreeSpace(string directoryPath)
    {
        var fullDirectory = Path.GetFullPath(directoryPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var directoryWithSeparator = EnsureTrailingDirectorySeparator(fullDirectory);

        var drive = DriveInfo.GetDrives()
            .Where(candidate => directoryWithSeparator.StartsWith(
                EnsureTrailingDirectorySeparator(Path.GetFullPath(candidate.RootDirectory.FullName)),
                comparison))
            .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
            .FirstOrDefault()
            ?? throw new IOException("The database storage volume could not be determined.");

        return drive.AvailableFreeSpace;
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static int[] ReadEmbeddedMigrationVersions()
    {
        const string marker = ".Migrations.";
        var versions = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(name => name.Contains(marker, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => name[(name.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length)..])
            .Select(fileName => fileName.Split('_', 2)[0])
            .Select(prefix => int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                ? version
                : throw new InvalidOperationException($"Embedded migration has an invalid version prefix: {prefix}."))
            .Order()
            .ToArray();

        if (versions.Length == 0)
        {
            throw new InvalidOperationException("No embedded database migrations were found for readiness checks.");
        }

        return versions;
    }
}
