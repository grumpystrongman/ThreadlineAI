using Microsoft.Data.Sqlite;

namespace Threadline.Infrastructure.Sqlite;

public sealed record SqliteStartupRecoveryResult(bool Recovered, string Detail, string? BackupPath = null);

public static class SqliteStartupRecovery
{
    public static async Task<SqliteStartupRecoveryResult> RecoverIfNeededAsync(SqliteOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath) || !File.Exists(options.DatabasePath))
        {
            return new SqliteStartupRecoveryResult(false, "Database file does not exist yet.");
        }

        try
        {
            await using var connection = new SqliteConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new SqliteStartupRecoveryResult(false, "SQLite integrity check passed.");
            }

            return MoveAside(options.DatabasePath, $"SQLite integrity check failed: {result}");
        }
        catch (SqliteException ex)
        {
            return MoveAside(options.DatabasePath, $"SQLite open failed during startup recovery: {ex.SqliteErrorCode} {ex.Message}");
        }
    }

    private static SqliteStartupRecoveryResult MoveAside(string databasePath, string detail)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = databasePath + $".corrupt-{timestamp}";
        File.Move(databasePath, backupPath, overwrite: true);
        TryMove(databasePath + "-wal", backupPath + "-wal");
        TryMove(databasePath + "-shm", backupPath + "-shm");
        return new SqliteStartupRecoveryResult(true, detail, backupPath);
    }

    private static void TryMove(string source, string destination)
    {
        if (!File.Exists(source)) return;
        try
        {
            File.Move(source, destination, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup. The main database has already been moved aside.
        }
    }
}
