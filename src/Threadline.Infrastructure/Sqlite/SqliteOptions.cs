namespace Threadline.Infrastructure.Sqlite;

public sealed record SqliteOptions(string ConnectionString, string DatabasePath)
{
    public static SqliteOptions LocalAppData(string? databasePath = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI");

        Directory.CreateDirectory(root);
        var path = string.IsNullOrWhiteSpace(databasePath) ? Path.Combine(root, "threadline.db") : databasePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        return new SqliteOptions($"Data Source={path}", path);
    }
}
