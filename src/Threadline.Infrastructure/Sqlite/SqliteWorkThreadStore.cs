using Microsoft.Data.Sqlite;
using Threadline.Core;

namespace Threadline.Infrastructure.Sqlite;

public sealed class SqliteWorkThreadStore : IWorkThreadRepository, IThreadlineStoreInitializer
{
    private readonly SqliteOptions _options;

    public SqliteWorkThreadStore(SqliteOptions options) => _options = options;

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

    public async Task<WorkThread?> GetWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Description, Status, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, LastResumedAtUtc
            FROM WorkThreads
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", workThreadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWorkThread(reader) : null;
    }

    public async Task<WorkThread?> GetActiveWorkThreadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Description, Status, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, LastResumedAtUtc
            FROM WorkThreads
            WHERE Status = $status
            ORDER BY COALESCE(LastResumedAtUtc, UpdatedAtUtc, CreatedAtUtc) DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$status", WorkThreadStatus.Open.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWorkThread(reader) : null;
    }

    public async Task<IReadOnlyList<WorkThread>> ListWorkThreadsAsync(int take = 25, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Description, Status, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, LastResumedAtUtc
            FROM WorkThreads
            ORDER BY COALESCE(LastResumedAtUtc, UpdatedAtUtc, CreatedAtUtc) DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<WorkThread>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadWorkThread(reader));
        }

        return results;
    }

    public async Task SaveWorkThreadAsync(WorkThread workThread, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkThreads (Id, Title, Description, Status, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, LastResumedAtUtc)
            VALUES ($id, $title, $description, $status, $createdAtUtc, $updatedAtUtc, $closedAtUtc, $lastResumedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Description = excluded.Description,
                Status = excluded.Status,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                ClosedAtUtc = excluded.ClosedAtUtc,
                LastResumedAtUtc = excluded.LastResumedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", workThread.Id);
        command.Parameters.AddWithValue("$title", workThread.Title);
        command.Parameters.AddWithValue("$description", ToDbValue(workThread.Description));
        command.Parameters.AddWithValue("$status", workThread.Status.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", ToText(workThread.CreatedAt));
        command.Parameters.AddWithValue("$updatedAtUtc", ToText(workThread.UpdatedAt));
        command.Parameters.AddWithValue("$closedAtUtc", ToNullableText(workThread.ClosedAt));
        command.Parameters.AddWithValue("$lastResumedAtUtc", ToNullableText(workThread.LastResumedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendWorkContextEventAsync(WorkContextEvent contextEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkContextEvents (Id, WorkThreadId, SourceType, SourceName, AppName, WindowTitle, Url, ContentSummary, CaptureMode, CreatedAtUtc)
            VALUES ($id, $workThreadId, $sourceType, $sourceName, $appName, $windowTitle, $url, $contentSummary, $captureMode, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$id", contextEvent.Id);
        command.Parameters.AddWithValue("$workThreadId", contextEvent.WorkThreadId);
        command.Parameters.AddWithValue("$sourceType", contextEvent.SourceType);
        command.Parameters.AddWithValue("$sourceName", contextEvent.SourceName);
        command.Parameters.AddWithValue("$appName", ToDbValue(contextEvent.AppName));
        command.Parameters.AddWithValue("$windowTitle", ToDbValue(contextEvent.WindowTitle));
        command.Parameters.AddWithValue("$url", ToDbValue(contextEvent.Url));
        command.Parameters.AddWithValue("$contentSummary", ToDbValue(contextEvent.ContentSummary));
        command.Parameters.AddWithValue("$captureMode", contextEvent.CaptureMode.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", ToText(contextEvent.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkContextEvent>> GetRecentWorkContextEventsAsync(string workThreadId, int take = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkThreadId, SourceType, SourceName, AppName, WindowTitle, Url, ContentSummary, CaptureMode, CreatedAtUtc
            FROM WorkContextEvents
            WHERE WorkThreadId = $workThreadId
            ORDER BY CreatedAtUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$workThreadId", workThreadId);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<WorkContextEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadWorkContextEvent(reader));
        }
        results.Reverse();
        return results;
    }

    public async Task AppendConversationMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConversationMessages (Id, WorkThreadId, Role, Content, CreatedAtUtc, ContextReceiptId)
            VALUES ($id, $workThreadId, $role, $content, $createdAtUtc, $contextReceiptId);
            """;
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$workThreadId", message.WorkThreadId);
        command.Parameters.AddWithValue("$role", message.Role);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$createdAtUtc", ToText(message.CreatedAt));
        command.Parameters.AddWithValue("$contextReceiptId", ToDbValue(message.ContextReceiptId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(string workThreadId, int take = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkThreadId, Role, Content, CreatedAtUtc, ContextReceiptId
            FROM ConversationMessages
            WHERE WorkThreadId = $workThreadId
            ORDER BY CreatedAtUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$workThreadId", workThreadId);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<ConversationMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadConversationMessage(reader));
        }
        results.Reverse();
        return results;
    }

    public async Task SaveContextReceiptAsync(ContextReceiptRecord contextReceipt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContextReceipts (Id, WorkThreadId, UsedSourcesJson, NotUsedSourcesJson, Limitations, CreatedAtUtc)
            VALUES ($id, $workThreadId, $usedSourcesJson, $notUsedSourcesJson, $limitations, $createdAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                UsedSourcesJson = excluded.UsedSourcesJson,
                NotUsedSourcesJson = excluded.NotUsedSourcesJson,
                Limitations = excluded.Limitations;
            """;
        command.Parameters.AddWithValue("$id", contextReceipt.Id);
        command.Parameters.AddWithValue("$workThreadId", contextReceipt.WorkThreadId);
        command.Parameters.AddWithValue("$usedSourcesJson", contextReceipt.UsedSourcesJson);
        command.Parameters.AddWithValue("$notUsedSourcesJson", ToDbValue(contextReceipt.NotUsedSourcesJson));
        command.Parameters.AddWithValue("$limitations", ToDbValue(contextReceipt.Limitations));
        command.Parameters.AddWithValue("$createdAtUtc", ToText(contextReceipt.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ContextReceiptRecord?> GetContextReceiptAsync(string contextReceiptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkThreadId, UsedSourcesJson, NotUsedSourcesJson, Limitations, CreatedAtUtc
            FROM ContextReceipts
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", contextReceiptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadContextReceipt(reader) : null;
    }

    public async Task SaveArtifactAsync(WorkArtifact artifact, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkArtifacts (Id, WorkThreadId, ArtifactType, Title, Content, CreatedAtUtc, UpdatedAtUtc, ContextReceiptId)
            VALUES ($id, $workThreadId, $artifactType, $title, $content, $createdAtUtc, $updatedAtUtc, $contextReceiptId)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Content = excluded.Content,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                ContextReceiptId = excluded.ContextReceiptId;
            """;
        command.Parameters.AddWithValue("$id", artifact.Id);
        command.Parameters.AddWithValue("$workThreadId", artifact.WorkThreadId);
        command.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
        command.Parameters.AddWithValue("$title", artifact.Title);
        command.Parameters.AddWithValue("$content", artifact.Content);
        command.Parameters.AddWithValue("$createdAtUtc", ToText(artifact.CreatedAt));
        command.Parameters.AddWithValue("$updatedAtUtc", ToText(artifact.UpdatedAt));
        command.Parameters.AddWithValue("$contextReceiptId", ToDbValue(artifact.ContextReceiptId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkArtifact>> GetArtifactsAsync(string workThreadId, int take = 25, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkThreadId, ArtifactType, Title, Content, CreatedAtUtc, UpdatedAtUtc, ContextReceiptId
            FROM WorkArtifacts
            WHERE WorkThreadId = $workThreadId
            ORDER BY UpdatedAtUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$workThreadId", workThreadId);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<WorkArtifact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadArtifact(reader));
        }
        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static WorkThread ReadWorkThread(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        Enum.Parse<WorkThreadStatus>(reader.GetString(3)),
        FromText(reader.GetString(4)),
        FromText(reader.GetString(5)),
        reader.IsDBNull(6) ? null : FromText(reader.GetString(6)),
        reader.IsDBNull(7) ? null : FromText(reader.GetString(7)));

    private static WorkContextEvent ReadWorkContextEvent(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        Enum.Parse<WorkCaptureMode>(reader.GetString(8)),
        FromText(reader.GetString(9)));

    private static ConversationMessage ReadConversationMessage(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        FromText(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetString(5));

    private static ContextReceiptRecord ReadContextReceipt(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        FromText(reader.GetString(5)));

    private static WorkArtifact ReadArtifact(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        FromText(reader.GetString(5)),
        FromText(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7));

    private static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    private static object ToNullableText(DateTimeOffset? value) => value is null ? DBNull.Value : ToText(value.Value);
    private static object ToDbValue(string? value) => value is null ? DBNull.Value : value;
    private static DateTimeOffset FromText(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS WorkThreads (
            Id TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            Status TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            ClosedAtUtc TEXT NULL,
            LastResumedAtUtc TEXT NULL
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_WorkThreads_Status_Updated ON WorkThreads(Status, UpdatedAtUtc);
        """,
        """
        CREATE TABLE IF NOT EXISTS WorkContextEvents (
            Id TEXT PRIMARY KEY,
            WorkThreadId TEXT NOT NULL,
            SourceType TEXT NOT NULL,
            SourceName TEXT NOT NULL,
            AppName TEXT NULL,
            WindowTitle TEXT NULL,
            Url TEXT NULL,
            ContentSummary TEXT NULL,
            CaptureMode TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY(WorkThreadId) REFERENCES WorkThreads(Id)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_WorkContextEvents_Thread_Created ON WorkContextEvents(WorkThreadId, CreatedAtUtc);
        """,
        """
        CREATE TABLE IF NOT EXISTS ContextReceipts (
            Id TEXT PRIMARY KEY,
            WorkThreadId TEXT NOT NULL,
            UsedSourcesJson TEXT NOT NULL,
            NotUsedSourcesJson TEXT NULL,
            Limitations TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY(WorkThreadId) REFERENCES WorkThreads(Id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS ConversationMessages (
            Id TEXT PRIMARY KEY,
            WorkThreadId TEXT NOT NULL,
            Role TEXT NOT NULL,
            Content TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            ContextReceiptId TEXT NULL,
            FOREIGN KEY(WorkThreadId) REFERENCES WorkThreads(Id),
            FOREIGN KEY(ContextReceiptId) REFERENCES ContextReceipts(Id)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_ConversationMessages_Thread_Created ON ConversationMessages(WorkThreadId, CreatedAtUtc);
        """,
        """
        CREATE TABLE IF NOT EXISTS WorkArtifacts (
            Id TEXT PRIMARY KEY,
            WorkThreadId TEXT NOT NULL,
            ArtifactType TEXT NOT NULL,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            ContextReceiptId TEXT NULL,
            FOREIGN KEY(WorkThreadId) REFERENCES WorkThreads(Id),
            FOREIGN KEY(ContextReceiptId) REFERENCES ContextReceipts(Id)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_WorkArtifacts_Thread_Updated ON WorkArtifacts(WorkThreadId, UpdatedAtUtc);
        """
    ];
}
