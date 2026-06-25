using Microsoft.Data.Sqlite;
using Threadline.Core;

namespace Threadline.Infrastructure.Sqlite;

/// <summary>
/// Shared reader methods that map SqliteDataReader rows to work-thread domain records.
/// These are extracted from SqliteWorkThreadStore and SqlitePrivacyAndMaintenanceStore
/// where they were duplicated identically.
/// </summary>
internal static class SqliteWorkThreadReaders
{
    public static WorkThread ReadWorkThread(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        Enum.Parse<WorkThreadStatus>(reader.GetString(3)),
        SqliteHelpers.FromText(reader.GetString(4)),
        SqliteHelpers.FromText(reader.GetString(5)),
        reader.IsDBNull(6) ? null : SqliteHelpers.FromText(reader.GetString(6)),
        reader.IsDBNull(7) ? null : SqliteHelpers.FromText(reader.GetString(7)));

    public static WorkContextEvent ReadWorkContextEvent(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        Enum.Parse<WorkCaptureMode>(reader.GetString(8)),
        SqliteHelpers.FromText(reader.GetString(9)));

    public static ConversationMessage ReadConversationMessage(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        SqliteHelpers.FromText(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetString(5));

    public static ContextReceiptRecord ReadContextReceipt(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        SqliteHelpers.FromText(reader.GetString(5)));

    public static WorkArtifact ReadArtifact(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        SqliteHelpers.FromText(reader.GetString(5)),
        SqliteHelpers.FromText(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7));
}
