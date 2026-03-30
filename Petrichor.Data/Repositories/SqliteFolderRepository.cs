using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Data.Persistence;

namespace Petrichor.Data.Repositories;

public sealed class SqliteFolderRepository : IFolderRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteFolderRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FolderReference>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, name, track_count
            FROM folders
            ORDER BY name;
            """;

        var results = new List<FolderReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new FolderReference(
                Id: reader.GetInt64(0),
                Path: reader.GetString(1),
                Name: reader.GetString(2),
                TrackCount: reader.GetInt32(3)));
        }

        return results;
    }

    public async Task<FolderReference?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, name, track_count
            FROM folders
            WHERE path = $path
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$path", path);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FolderReference(
            Id: reader.GetInt64(0),
            Path: reader.GetString(1),
            Name: reader.GetString(2),
            TrackCount: reader.GetInt32(3));
    }

    public async Task UpsertAsync(FolderReference folder, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO folders (name, path, track_count, date_added, date_updated, bookmark_data)
            VALUES ($name, $path, $trackCount, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL)
            ON CONFLICT(path) DO UPDATE SET
                name = excluded.name,
                track_count = excluded.track_count,
                date_updated = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$name", folder.Name);
        command.Parameters.AddWithValue("$path", folder.Path);
        command.Parameters.AddWithValue("$trackCount", folder.TrackCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM folders WHERE path = $path;";
        command.Parameters.AddWithValue("$path", path);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
