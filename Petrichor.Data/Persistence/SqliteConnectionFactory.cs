using Microsoft.Data.Sqlite;

namespace Petrichor.Data.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databaseFilePath)
    {
        connectionString = $"Data Source={databaseFilePath}";
    }

    public SqliteConnection Create()
    {
        return new SqliteConnection(connectionString);
    }
}
