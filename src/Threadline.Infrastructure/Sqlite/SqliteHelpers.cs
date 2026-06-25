using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Threadline.Infrastructure.Sqlite;

/// <summary>
/// Shared helper methods for SQLite date/time, nullable, and JSON conversions
/// used across all Threadline SQLite stores.
/// </summary>
public static class SqliteHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    public static object ToNullableText(DateTimeOffset? value) => value is null ? DBNull.Value : ToText(value.Value);

    public static object ToDbValue(string? value) => value is null ? DBNull.Value : value;

    public static object ToTrimmedDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    public static object ToJsonDbValue(IReadOnlyDictionary<string, string>? value) =>
        value is null ? DBNull.Value : JsonSerializer.Serialize(value, JsonOptions);

    public static DateTimeOffset FromText(string value) =>
        DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    public static IReadOnlyDictionary<string, string>? FromJson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions);

    public static async Task<SqliteConnection> OpenConnectionAsync(SqliteOptions options, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public static async Task<SqliteConnection> OpenConnectionWithPragmasAsync(SqliteOptions options, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var pragma in DefaultPragmas)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return connection;
    }

    public static async Task ExecuteSchemaAsync(SqliteConnection connection, string[] statements, CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static readonly string[] DefaultPragmas =
    [
        "PRAGMA busy_timeout=5000;",
        "PRAGMA foreign_keys=ON;",
        "PRAGMA journal_mode=WAL;",
        "PRAGMA synchronous=FULL;"
    ];
}
