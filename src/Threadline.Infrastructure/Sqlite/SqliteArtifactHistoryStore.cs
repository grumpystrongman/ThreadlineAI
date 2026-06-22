using Microsoft.Data.Sqlite;
using Threadline.Core;

namespace Threadline.Infrastructure.Sqlite;

public sealed class SqliteArtifactHistoryStore : IArtifactHistoryRepository, IThreadlineStoreInitializer
{
    private readonly SqliteOptions _options;

    public SqliteArtifactHistoryStore(SqliteOptions options) => _options = options;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var statement in SchemaStatements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task SaveArtifactVersionAsync(WorkArtifact artifact, string operation, string? actionId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var version = await GetNextVersionAsync(connection, artifact.Id, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkArtifactHistory (Id, ArtifactId, WorkThreadId, Version, ArtifactType, Title, Content, Operation, ActionId, CreatedAtUtc, ContextReceiptId)
            VALUES ($id, $artifactId, $workThreadId, $version, $artifactType, $title, $content, $operation, $actionId, $createdAtUtc, $contextReceiptId);
            """;
        command.Parameters.AddWithValue("$id", $"arv_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("$artifactId", artifact.Id);
        command.Parameters.AddWithValue("$workThreadId", artifact.WorkThreadId);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
        command.Parameters.AddWithValue("$title", artifact.Title);
        command.Parameters.AddWithValue("$content", artifact.Content);
        command.Parameters.AddWithValue("$operation", Normalize(operation, "generated"));
        command.Parameters.AddWithValue("$actionId", ToDbValue(actionId));
        command.Parameters.AddWithValue("$createdAtUtc", ToText(artifact.UpdatedAt));
        command.Parameters.AddWithValue("$contextReceiptId", ToDbValue(artifact.ContextReceiptId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkArtifactVersion>> GetArtifactHistoryAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artifactId)) return Array.Empty<WorkArtifactVersion>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ArtifactId, WorkThreadId, Version, ArtifactType, Title, Content, Operation, ActionId, CreatedAtUtc, ContextReceiptId
            FROM WorkArtifactHistory
            WHERE ArtifactId = $artifactId
            ORDER BY Version ASC, CreatedAtUtc ASC;
            """;
        command.Parameters.AddWithValue("$artifactId", artifactId.Trim());

        var results = new List<WorkArtifactVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadArtifactVersion(reader));
        }

        return results;
    }

    private async Task<int> GetNextVersionAsync(SqliteConnection connection, string artifactId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM WorkArtifactHistory WHERE ArtifactId = $artifactId;";
        command.Parameters.AddWithValue("$artifactId", artifactId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static WorkArtifactVersion ReadArtifactVersion(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        FromText(reader.GetString(9)),
        reader.IsDBNull(10) ? null : reader.GetString(10));

    private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    private static object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static DateTimeOffset FromText(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS WorkArtifactHistory (
            Id TEXT PRIMARY KEY,
            ArtifactId TEXT NOT NULL,
            WorkThreadId TEXT NOT NULL,
            Version INTEGER NOT NULL,
            ArtifactType TEXT NOT NULL,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            Operation TEXT NOT NULL,
            ActionId TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            ContextReceiptId TEXT NULL
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_WorkArtifactHistory_Artifact_Version ON WorkArtifactHistory(ArtifactId, Version);
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_WorkArtifactHistory_Thread_Created ON WorkArtifactHistory(WorkThreadId, CreatedAtUtc);
        """
    ];
}

public sealed class SqliteWorkContinuityMaintenanceStore : IThreadlineStoreInitializer
{
    private readonly SqliteOptions _options;

    public SqliteWorkContinuityMaintenanceStore(SqliteOptions options) => _options = options;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<int> ClearContextAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workThreadId)) return 0;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var affected = 0;
        affected += await ExecuteAsync(connection, "UPDATE ConversationMessages SET ContextReceiptId = NULL WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "UPDATE WorkArtifacts SET ContextReceiptId = NULL WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM ContextReceipts WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM WorkContextEvents WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        return affected;
    }

    public async Task<int> ClearConversationAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workThreadId)) return 0;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ExecuteAsync(connection, "DELETE FROM ConversationMessages WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
    }

    public async Task<int> ClearMemoryAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workThreadId)) return 0;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var affected = 0;
        affected += await ExecuteAsync(connection, "DELETE FROM WorkArtifactHistory WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM WorkArtifacts WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM ConversationMessages WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM WorkContextEvents WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM ContextReceipts WHERE WorkThreadId = $workThreadId;", workThreadId, cancellationToken);
        affected += await ExecuteAsync(connection, "DELETE FROM WorkThreads WHERE Id = $workThreadId;", workThreadId, cancellationToken);
        return affected;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<int> ExecuteAsync(SqliteConnection connection, string sql, string workThreadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$workThreadId", workThreadId.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
