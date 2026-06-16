namespace Threadline.Infrastructure.Sqlite;

public sealed record SqliteOptions(string ConnectionString)
{
    public static SqliteOptions LocalAppData(string? databasePath = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI");

        Directory.CreateDirectory(root);
        var path = databasePath ?? Path.Combine(root, "threadline.db");
        return new SqliteOptions($"Data Source={path}");
    }
}
