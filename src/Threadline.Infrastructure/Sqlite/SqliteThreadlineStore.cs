using System.Text.Json;
using Microsoft.Data.Sqlite;
using Threadline.Core;

namespace Threadline.Infrastructure.Sqlite;

public sealed class SqliteThreadlineStore : ISessionRepository, IProviderConnectionRepository, IAuditRepository, IThreadlineStoreInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteOptions _options;

    public SqliteThreadlineStore(SqliteOptions options) => _options = options;

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

    public async Task<ThreadlineSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, CreatedAtUtc, Status, ActiveProvider, EndedAtUtc
            FROM Sessions
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task<ThreadlineSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, CreatedAtUtc, Status, ActiveProvider, EndedAtUtc
            FROM Sessions
            WHERE Status = $status
            ORDER BY CreatedAtUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$status", SessionStatus.Active.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task SaveSessionAsync(ThreadlineSession session, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sessions (Id, Name, CreatedAtUtc, Status, ActiveProvider, EndedAtUtc)
            VALUES ($id, $name, $createdAtUtc, $status, $activeProvider, $endedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Status = excluded.Status,
                ActiveProvider = excluded.ActiveProvider,
                EndedAtUtc = excluded.EndedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$createdAtUtc", ToText(session.CreatedAt));
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$activeProvider", ToDbValue(session.ActiveProvider));
        command.Parameters.AddWithValue("$endedAtUtc", ToNullableText(session.EndedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendEventAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContextEvents (Id, SessionId, TimestampUtc, Source, ContextType, Content, ApplicationName, ProcessName, WindowTitle, Uri, Sensitivity, UserApproved, MetadataJson)
            VALUES ($id, $sessionId, $timestampUtc, $source, $contextType, $content, $applicationName, $processName, $windowTitle, $uri, $sensitivity, $userApproved, $metadataJson);
            """;
        command.Parameters.AddWithValue("$id", contextEvent.Id);
        command.Parameters.AddWithValue("$sessionId", contextEvent.SessionId);
        command.Parameters.AddWithValue("$timestampUtc", ToText(contextEvent.Timestamp));
        command.Parameters.AddWithValue("$source", contextEvent.Source.ToString());
        command.Parameters.AddWithValue("$contextType", contextEvent.ContextType);
        command.Parameters.AddWithValue("$content", contextEvent.Content);
        command.Parameters.AddWithValue("$applicationName", ToDbValue(contextEvent.ApplicationName));
        command.Parameters.AddWithValue("$processName", ToDbValue(contextEvent.ProcessName));
        command.Parameters.AddWithValue("$windowTitle", ToDbValue(contextEvent.WindowTitle));
        command.Parameters.AddWithValue("$uri", ToDbValue(contextEvent.Uri));
        command.Parameters.AddWithValue("$sensitivity", contextEvent.Sensitivity.ToString());
        command.Parameters.AddWithValue("$userApproved", contextEvent.UserApproved ? 1 : 0);
        command.Parameters.AddWithValue("$metadataJson", ToJsonDbValue(contextEvent.Metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContextEvent>> GetRecentEventsAsync(string sessionId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, TimestampUtc, Source, ContextType, Content, ApplicationName, ProcessName, WindowTitle, Uri, Sensitivity, UserApproved, MetadataJson
            FROM ContextEvents
            WHERE SessionId = $sessionId
            ORDER BY TimestampUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<ContextEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadContextEvent(reader));
        }

        results.Reverse();
        return results;
    }

    public async Task SaveSummaryAsync(string sessionId, string summary, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SessionSummaries (Id, SessionId, CreatedAtUtc, Summary)
            VALUES ($id, $sessionId, $createdAtUtc, $summary);
            """;
        command.Parameters.AddWithValue("$id", $"sum_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$createdAtUtc", ToText(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$summary", summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetLatestSummaryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Summary
            FROM SessionSummaries
            WHERE SessionId = $sessionId
            ORDER BY CreatedAtUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task SaveProviderConnectionAsync(ProviderConnection connectionRecord, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProviderConnections (Id, ProviderName, AuthType, CredentialReference, BaseUrl, DefaultModel, Status, CreatedAtUtc, UpdatedAtUtc, MetadataJson)
            VALUES ($id, $providerName, $authType, $credentialReference, $baseUrl, $defaultModel, $status, $createdAtUtc, $updatedAtUtc, $metadataJson)
            ON CONFLICT(ProviderName) DO UPDATE SET
                AuthType = excluded.AuthType,
                CredentialReference = excluded.CredentialReference,
                BaseUrl = excluded.BaseUrl,
                DefaultModel = excluded.DefaultModel,
                Status = excluded.Status,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                MetadataJson = excluded.MetadataJson;
            """;
        command.Parameters.AddWithValue("$id", connectionRecord.Id);
        command.Parameters.AddWithValue("$providerName", connectionRecord.ProviderName);
        command.Parameters.AddWithValue("$authType", connectionRecord.AuthType.ToString());
        command.Parameters.AddWithValue("$credentialReference", ToDbValue(connectionRecord.CredentialReference));
        command.Parameters.AddWithValue("$baseUrl", ToDbValue(connectionRecord.BaseUrl));
        command.Parameters.AddWithValue("$defaultModel", ToDbValue(connectionRecord.DefaultModel));
        command.Parameters.AddWithValue("$status", connectionRecord.Status.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", ToText(connectionRecord.CreatedAt));
        command.Parameters.AddWithValue("$updatedAtUtc", ToNullableText(connectionRecord.UpdatedAt));
        command.Parameters.AddWithValue("$metadataJson", ToJsonDbValue(connectionRecord.Metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProviderConnection?> GetProviderConnectionAsync(string providerName, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProviderName, AuthType, CredentialReference, BaseUrl, DefaultModel, Status, CreatedAtUtc, UpdatedAtUtc, MetadataJson
            FROM ProviderConnections
            WHERE ProviderName = $providerName;
            """;
        command.Parameters.AddWithValue("$providerName", providerName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProviderConnection(reader) : null;
    }

    public async Task<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProviderName, AuthType, CredentialReference, BaseUrl, DefaultModel, Status, CreatedAtUtc, UpdatedAtUtc, MetadataJson
            FROM ProviderConnections
            ORDER BY ProviderName;
            """;
        var results = new List<ProviderConnection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadProviderConnection(reader));
        }
        return results;
    }

    public async Task AppendAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AuditEvents (Id, SessionId, EventType, TimestampUtc, Message, MetadataJson)
            VALUES ($id, $sessionId, $eventType, $timestampUtc, $message, $metadataJson);
            """;
        command.Parameters.AddWithValue("$id", auditEvent.Id);
        command.Parameters.AddWithValue("$sessionId", ToDbValue(auditEvent.SessionId));
        command.Parameters.AddWithValue("$eventType", auditEvent.EventType.ToString());
        command.Parameters.AddWithValue("$timestampUtc", ToText(auditEvent.Timestamp));
        command.Parameters.AddWithValue("$message", auditEvent.Message);
        command.Parameters.AddWithValue("$metadataJson", ToJsonDbValue(auditEvent.Metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentAuditEventsAsync(string? sessionId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sessionId is null
            ? """
              SELECT Id, SessionId, EventType, TimestampUtc, Message, MetadataJson
              FROM AuditEvents
              ORDER BY TimestampUtc DESC
              LIMIT $take;
              """
            : """
              SELECT Id, SessionId, EventType, TimestampUtc, Message, MetadataJson
              FROM AuditEvents
              WHERE SessionId = $sessionId
              ORDER BY TimestampUtc DESC
              LIMIT $take;
              """;
        if (sessionId is not null) command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<AuditEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAuditEvent(reader));
        }
        results.Reverse();
        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static ThreadlineSession ReadSession(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        FromText(reader.GetString(2)),
        Enum.Parse<SessionStatus>(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : FromText(reader.GetString(5)));

    private static ContextEvent ReadContextEvent(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        FromText(reader.GetString(2)),
        Enum.Parse<ContextSource>(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        Enum.Parse<SensitivityLevel>(reader.GetString(10)),
        reader.GetInt32(11) == 1,
        FromJson(reader.IsDBNull(12) ? null : reader.GetString(12)));

    private static ProviderConnection ReadProviderConnection(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        Enum.Parse<ProviderAuthType>(reader.GetString(2)),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        Enum.Parse<ProviderConnectionStatus>(reader.GetString(6)),
        FromText(reader.GetString(7)),
        reader.IsDBNull(8) ? null : FromText(reader.GetString(8)),
        FromJson(reader.IsDBNull(9) ? null : reader.GetString(9)));

    private static AuditEvent ReadAuditEvent(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        Enum.Parse<AuditEventType>(reader.GetString(2)),
        FromText(reader.GetString(3)),
        reader.GetString(4),
        FromJson(reader.IsDBNull(5) ? null : reader.GetString(5)));

    private static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    private static object ToNullableText(DateTimeOffset? value) => value is null ? DBNull.Value : ToText(value.Value);
    private static object ToDbValue(string? value) => value is null ? DBNull.Value : value;
    private static object ToJsonDbValue(IReadOnlyDictionary<string, string>? value) => value is null ? DBNull.Value : JsonSerializer.Serialize(value, JsonOptions);
    private static DateTimeOffset FromText(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static IReadOnlyDictionary<string, string>? FromJson(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions);

    private static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS Sessions (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            Status TEXT NOT NULL,
            ActiveProvider TEXT NULL,
            EndedAtUtc TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS ContextEvents (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NOT NULL,
            TimestampUtc TEXT NOT NULL,
            Source TEXT NOT NULL,
            ContextType TEXT NOT NULL,
            Content TEXT NOT NULL,
            ApplicationName TEXT NULL,
            ProcessName TEXT NULL,
            WindowTitle TEXT NULL,
            Uri TEXT NULL,
            Sensitivity TEXT NOT NULL,
            UserApproved INTEGER NOT NULL,
            MetadataJson TEXT NULL,
            FOREIGN KEY(SessionId) REFERENCES Sessions(Id)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_ContextEvents_Session_Timestamp ON ContextEvents(SessionId, TimestampUtc);
        """,
        """
        CREATE TABLE IF NOT EXISTS SessionSummaries (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            Summary TEXT NOT NULL,
            FOREIGN KEY(SessionId) REFERENCES Sessions(Id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS ProviderConnections (
            Id TEXT PRIMARY KEY,
            ProviderName TEXT NOT NULL UNIQUE,
            AuthType TEXT NOT NULL,
            CredentialReference TEXT NULL,
            BaseUrl TEXT NULL,
            DefaultModel TEXT NULL,
            Status TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NULL,
            MetadataJson TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS AuditEvents (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NULL,
            EventType TEXT NOT NULL,
            TimestampUtc TEXT NOT NULL,
            Message TEXT NOT NULL,
            MetadataJson TEXT NULL
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS IX_AuditEvents_Session_Timestamp ON AuditEvents(SessionId, TimestampUtc);
        """
    ];
}
