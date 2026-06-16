namespace Threadline.Infrastructure.Sqlite;

public interface IThreadlineStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
