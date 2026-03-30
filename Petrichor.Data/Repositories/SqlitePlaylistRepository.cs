using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Data.Persistence;

namespace Petrichor.Data.Repositories;

public sealed class SqlitePlaylistRepository : IPlaylistRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePlaylistRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PlaylistReference>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.id,
                   p.name,
                   p.type,
                   p.is_user_editable,
                   p.is_content_editable,
                   p.smart_criteria,
                   p.sort_order,
                   COUNT(pt.track_id)
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            GROUP BY p.id, p.name, p.type, p.is_user_editable, p.is_content_editable, p.smart_criteria, p.sort_order
            ORDER BY p.sort_order, p.date_created;
            """;

        var results = new List<PlaylistReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapPlaylist(reader));
        }

        return results;
    }

    public async Task<PlaylistReference?> GetByIdAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.id,
                   p.name,
                   p.type,
                   p.is_user_editable,
                   p.is_content_editable,
                   p.smart_criteria,
                   p.sort_order,
                   COUNT(pt.track_id)
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            WHERE p.id = $id
            GROUP BY p.id, p.name, p.type, p.is_user_editable, p.is_content_editable, p.smart_criteria, p.sort_order
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", playlistId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapPlaylist(reader) : null;
    }

    public async Task UpsertAsync(PlaylistReference playlist, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO playlists (
                id, name, type, is_user_editable, is_content_editable, date_created, date_modified, smart_criteria, sort_order
            )
            VALUES (
                $id, $name, $type, $isUserEditable, $isContentEditable, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, $smartCriteria, $sortOrder
            )
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                type = excluded.type,
                is_user_editable = excluded.is_user_editable,
                is_content_editable = excluded.is_content_editable,
                smart_criteria = excluded.smart_criteria,
                date_modified = CURRENT_TIMESTAMP,
                sort_order = excluded.sort_order;
            """;
        command.Parameters.AddWithValue("$id", playlist.Id.ToString());
        command.Parameters.AddWithValue("$name", playlist.Name);
        command.Parameters.AddWithValue("$type", playlist.Type);
        command.Parameters.AddWithValue("$isUserEditable", playlist.IsUserEditable);
        command.Parameters.AddWithValue("$isContentEditable", playlist.IsContentEditable);
        command.Parameters.AddWithValue("$smartCriteria", (object?)playlist.SmartCriteriaJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", playlist.SortOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var deleteTracksCommand = connection.CreateCommand();
        deleteTracksCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        deleteTracksCommand.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $playlistId;";
        deleteTracksCommand.Parameters.AddWithValue("$playlistId", playlistId.ToString());
        await deleteTracksCommand.ExecuteNonQueryAsync(cancellationToken);

        var deletePlaylistCommand = connection.CreateCommand();
        deletePlaylistCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        deletePlaylistCommand.CommandText = "DELETE FROM playlists WHERE id = $playlistId;";
        deletePlaylistCommand.Parameters.AddWithValue("$playlistId", playlistId.ToString());
        await deletePlaylistCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static PlaylistReference MapPlaylist(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new PlaylistReference(
            Id: Guid.Parse(reader.GetString(0)),
            Name: reader.GetString(1),
            Type: reader.GetString(2),
            IsUserEditable: reader.GetBoolean(3),
            IsContentEditable: reader.GetBoolean(4),
            TrackCount: reader.GetInt32(7),
            SortOrder: reader.GetInt32(6),
            SmartCriteriaJson: reader.IsDBNull(5) ? null : reader.GetString(5));
    }
}
