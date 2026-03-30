using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Data.Persistence;

namespace Petrichor.Data.Repositories;

public sealed class SqlitePlaylistTrackRepository : IPlaylistTrackRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePlaylistTrackRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TrackReference>> GetTracksForPlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.path, COALESCE(t.title, t.filename), t.artist, t.album, COALESCE(t.duration, 0), t.genre, t.year
            FROM playlist_tracks pt
            INNER JOIN tracks t ON t.id = pt.track_id
            WHERE pt.playlist_id = $playlistId
            ORDER BY pt.position;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId.ToString());

        var results = new List<TrackReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TrackReference(
                TrackId: reader.GetInt64(0),
                Path: reader.GetString(1),
                Title: reader.GetString(2),
                Artist: reader.IsDBNull(3) ? null : reader.GetString(3),
                Album: reader.IsDBNull(4) ? null : reader.GetString(4),
                DurationSeconds: reader.GetDouble(5),
                Genre: reader.IsDBNull(6) ? null : reader.GetString(6),
                Year: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    public async Task ReplaceTracksAsync(Guid playlistId, IEnumerable<long> trackIds, CancellationToken cancellationToken = default)
    {
        var ids = trackIds.ToArray();

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        deleteCommand.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $playlistId;";
        deleteCommand.Parameters.AddWithValue("$playlistId", playlistId.ToString());
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        for (var index = 0; index < ids.Length; index++)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insertCommand.CommandText =
                """
                INSERT INTO playlist_tracks (playlist_id, track_id, position, date_added)
                VALUES ($playlistId, $trackId, $position, CURRENT_TIMESTAMP);
                """;
            insertCommand.Parameters.AddWithValue("$playlistId", playlistId.ToString());
            insertCommand.Parameters.AddWithValue("$trackId", ids[index]);
            insertCommand.Parameters.AddWithValue("$position", index);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
