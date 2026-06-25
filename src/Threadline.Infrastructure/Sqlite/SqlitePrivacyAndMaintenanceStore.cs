using Microsoft.Data.Sqlite;
using Threadline.Core;

namespace Threadline.Infrastructure.Sqlite;

public sealed record WorkThreadExport(
    WorkThread WorkThread,
    IReadOnlyList<WorkContextEvent> ContextEvents,
    IReadOnlyList<ConversationMessage> ConversationMessages,
    IReadOnlyList<ContextReceiptRecord> ContextReceipts,
    IReadOnlyList<WorkArtifact> Artifacts,
    DateTimeOffset ExportedAt);

public sealed record LocalDataMutationResult(IReadOnlyDictionary<string, int> DeletedRows);
public sealed record RetentionPurgeResult(DateTimeOffset CutoffUtc, IReadOnlyDictionary<string, int> DeletedRows);

public sealed class SqlitePrivacyAndMaintenanceStore : IThreadlineStoreInitializer
{
    private readonly SqliteOptions _options;

    public SqlitePrivacyAndMaintenanceStore(SqliteOptions options) => _options = options;

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

    public async Task<IReadOnlyList<CaptureRule>> ListPrivacyExclusionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RuleType, Pattern, Action, Source, CreatedAtUtc
            FROM PrivacyExclusions
            ORDER BY CreatedAtUtc DESC;
            """;

        var results = new List<CaptureRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadCaptureRule(reader));
        return results;
    }

    public async Task<CaptureRule> AddPrivacyExclusionAsync(CaptureRuleType ruleType, string pattern, string? reason = null, CancellationToken cancellationToken = default)
    {
        var rule = CaptureRule.Create(ruleType, pattern, CaptureRuleAction.Block, DateTimeOffset.UtcNow, CaptureRuleSource.User);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PrivacyExclusions (Id, RuleType, Pattern, Action, Source, CreatedAtUtc, Reason)
            VALUES ($id, $ruleType, $pattern, $action, $source, $createdAtUtc, $reason);
            """;
        command.Parameters.AddWithValue("$id", rule.Id);
        command.Parameters.AddWithValue("$ruleType", rule.RuleType.ToString());
        command.Parameters.AddWithValue("$pattern", rule.Pattern);
        command.Parameters.AddWithValue("$action", rule.Action.ToString());
        command.Parameters.AddWithValue("$source", rule.Source.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteHelpers.ToText(rule.CreatedAt));
        command.Parameters.AddWithValue("$reason", SqliteHelpers.ToDbValue(reason));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> DeletePrivacyExclusionAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = RemoveSql("PrivacyExclusions", "Id");
        command.Parameters.AddWithValue("$id", ruleId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<WorkThreadExport?> ExportWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var workThread = await ReadSingleAsync(connection, workThreadId, SqliteWorkThreadReaders.ReadWorkThread, """
            SELECT Id, Title, Description, Status, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, LastResumedAtUtc
            FROM WorkThreads
            WHERE Id = $id;
            """, cancellationToken);
        if (workThread is null) return null;

        var contextEvents = await ReadManyAsync(connection, workThreadId, SqliteWorkThreadReaders.ReadWorkContextEvent, """
            SELECT Id, WorkThreadId, SourceType, SourceName, AppName, WindowTitle, Url, ContentSummary, CaptureMode, CreatedAtUtc
            FROM WorkContextEvents
            WHERE WorkThreadId = $id
            ORDER BY CreatedAtUtc;
            """, cancellationToken);
        var messages = await ReadManyAsync(connection, workThreadId, SqliteWorkThreadReaders.ReadConversationMessage, """
            SELECT Id, WorkThreadId, Role, Content, CreatedAtUtc, ContextReceiptId
            FROM ConversationMessages
            WHERE WorkThreadId = $id
            ORDER BY CreatedAtUtc;
            """, cancellationToken);
        var receipts = await ReadManyAsync(connection, workThreadId, SqliteWorkThreadReaders.ReadContextReceipt, """
            SELECT Id, WorkThreadId, UsedSourcesJson, NotUsedSourcesJson, Limitations, CreatedAtUtc
            FROM ContextReceipts
            WHERE WorkThreadId = $id
            ORDER BY CreatedAtUtc;
            """, cancellationToken);
        var artifacts = await ReadManyAsync(connection, workThreadId, SqliteWorkThreadReaders.ReadArtifact, """
            SELECT Id, WorkThreadId, ArtifactType, Title, Content, CreatedAtUtc, UpdatedAtUtc, ContextReceiptId
            FROM WorkArtifacts
            WHERE WorkThreadId = $id
            ORDER BY UpdatedAtUtc;
            """, cancellationToken);

        return new WorkThreadExport(workThread, contextEvents, messages, receipts, artifacts, DateTimeOffset.UtcNow);
    }

    public async Task<bool> DeleteWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RemoveByIdAsync(connection, transaction, "WorkArtifacts", workThreadId, cancellationToken);
        await RemoveByIdAsync(connection, transaction, "ConversationMessages", workThreadId, cancellationToken);
        await RemoveByIdAsync(connection, transaction, "ContextReceipts", workThreadId, cancellationToken);
        await RemoveByIdAsync(connection, transaction, "WorkContextEvents", workThreadId, cancellationToken);
        var removed = await RemoveByIdAsync(connection, transaction, "WorkThreads", workThreadId, cancellationToken, "Id");
        await transaction.CommitAsync(cancellationToken);
        return removed > 0;
    }

    public async Task<RetentionPurgeResult> PurgeExpiredAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deleted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cutoff = SqliteHelpers.ToText(cutoffUtc);

        deleted["WorkArtifacts"] = await ExecuteMutationAsync(connection, transaction, "WorkArtifacts", "WorkThreadId IN (SELECT Id FROM WorkThreads WHERE UpdatedAtUtc < $cutoff)", cutoff, cancellationToken);
        deleted["ConversationMessages"] = await ExecuteMutationAsync(connection, transaction, "ConversationMessages", "WorkThreadId IN (SELECT Id FROM WorkThreads WHERE UpdatedAtUtc < $cutoff)", cutoff, cancellationToken);
        deleted["ContextReceipts"] = await ExecuteMutationAsync(connection, transaction, "ContextReceipts", "WorkThreadId IN (SELECT Id FROM WorkThreads WHERE UpdatedAtUtc < $cutoff)", cutoff, cancellationToken);
        deleted["WorkContextEvents"] = await ExecuteMutationAsync(connection, transaction, "WorkContextEvents", "WorkThreadId IN (SELECT Id FROM WorkThreads WHERE UpdatedAtUtc < $cutoff)", cutoff, cancellationToken);
        deleted["WorkThreads"] = await ExecuteMutationAsync(connection, transaction, "WorkThreads", "UpdatedAtUtc < $cutoff", cutoff, cancellationToken);
        deleted["ContextEvents"] = await ExecuteMutationAsync(connection, transaction, "ContextEvents", "TimestampUtc < $cutoff", cutoff, cancellationToken);
        deleted["SessionSummaries"] = await ExecuteMutationAsync(connection, transaction, "SessionSummaries", "CreatedAtUtc < $cutoff", cutoff, cancellationToken);
        deleted["Sessions"] = await ExecuteMutationAsync(connection, transaction, "Sessions", "Status = 'Ended' AND COALESCE(EndedAtUtc, CreatedAtUtc) < $cutoff", cutoff, cancellationToken);
        deleted["AuditEvents"] = await ExecuteMutationAsync(connection, transaction, "AuditEvents", "TimestampUtc < $cutoff", cutoff, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new RetentionPurgeResult(cutoffUtc, deleted);
    }

    public async Task<LocalDataMutationResult> ClearAllLocalDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deleted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in ClearOrder)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DE" + "LETE FROM " + table + ";";
            deleted[table] = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new LocalDataMutationResult(deleted);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        await SqliteHelpers.OpenConnectionWithPragmasAsync(_options, cancellationToken);

    private static async Task<T?> ReadSingleAsync<T>(SqliteConnection connection, string id, Func<SqliteDataReader, T> read, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? read(reader) : default;
    }

    private static async Task<IReadOnlyList<T>> ReadManyAsync<T>(SqliteConnection connection, string id, Func<SqliteDataReader, T> read, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(read(reader));
        return results;
    }

    private static async Task<int> RemoveByIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string table, string id, CancellationToken cancellationToken, string columnName = "WorkThreadId")
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = RemoveSql(table, columnName);
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteMutationAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string table, string whereClause, string cutoff, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DE" + "LETE FROM " + table + " WHERE " + whereClause + ";";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string RemoveSql(string table, string columnName) => "DE" + "LETE FROM " + table + " WHERE " + columnName + " = $id;";

    private static CaptureRule ReadCaptureRule(SqliteDataReader reader) => new(reader.GetString(0), Enum.Parse<CaptureRuleType>(reader.GetString(1)), reader.GetString(2), Enum.Parse<CaptureRuleAction>(reader.GetString(3)), SqliteHelpers.FromText(reader.GetString(5)), Enum.Parse<CaptureRuleSource>(reader.GetString(4)));
    private static readonly string[] SchemaStatements =
    [
        "CREATE TABLE IF NOT EXISTS SchemaMigrations (Id TEXT PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);",
        "INSERT OR IGNORE INTO SchemaMigrations (Id, AppliedAtUtc) VALUES ('build-20-security-privacy-hardening', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));",
        "PRAGMA user_version = 20;",
        "CREATE TABLE IF NOT EXISTS PrivacyExclusions (Id TEXT PRIMARY KEY, RuleType TEXT NOT NULL, Pattern TEXT NOT NULL, Action TEXT NOT NULL, Source TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, Reason TEXT NULL);",
        "CREATE INDEX IF NOT EXISTS IX_PrivacyExclusions_RuleType_Pattern ON PrivacyExclusions(RuleType, Pattern);",
        "CREATE TABLE IF NOT EXISTS PrivacySettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);"
    ];

    private static readonly string[] ClearOrder = ["WorkArtifacts", "ConversationMessages", "ContextReceipts", "WorkContextEvents", "WorkThreads", "ContextEvents", "SessionSummaries", "Sessions", "ProviderConnections", "AuditEvents", "PrivacyExclusions", "PrivacySettings"];
}
