using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Data.Persistence;

namespace Petrichor.Data.Repositories;

public sealed class SqlitePinnedItemRepository : IPinnedItemRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePinnedItemRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PinnedItemReference>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   item_type,
                   filter_type,
                   filter_value,
                   entity_id,
                   playlist_id,
                   display_name,
                   icon_name,
                   sort_order
            FROM pinned_items
            ORDER BY sort_order, id;
            """;

        var results = new List<PinnedItemReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PinnedItemReference(
                Id: reader.GetInt64(0),
                ItemType: reader.GetString(1),
                FilterType: reader.IsDBNull(2) ? null : reader.GetString(2),
                FilterValue: reader.IsDBNull(3) ? null : reader.GetString(3),
                EntityId: TryParseGuid(reader, 4),
                PlaylistId: TryParseGuid(reader, 5),
                DisplayName: reader.GetString(6),
                IconName: reader.GetString(7),
                SortOrder: reader.GetInt32(8)));
        }

        return results;
    }

    public async Task UpsertAsync(PinnedItemReference item, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO pinned_items (
                item_type,
                filter_type,
                filter_value,
                entity_id,
                playlist_id,
                display_name,
                icon_name,
                sort_order,
                date_added
            )
            VALUES (
                $itemType,
                $filterType,
                $filterValue,
                $entityId,
                $playlistId,
                $displayName,
                $iconName,
                $sortOrder,
                CURRENT_TIMESTAMP
            );
            """;

        command.Parameters.AddWithValue("$itemType", item.ItemType);
        command.Parameters.AddWithValue("$filterType", (object?)item.FilterType ?? DBNull.Value);
        command.Parameters.AddWithValue("$filterValue", (object?)item.FilterValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$entityId", (object?)item.EntityId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$playlistId", (object?)item.PlaylistId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$displayName", item.DisplayName);
        command.Parameters.AddWithValue("$iconName", item.IconName);
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(long pinnedItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pinned_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", pinnedItemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReorderAsync(IReadOnlyList<long> pinnedItemIdsInOrder, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        for (var index = 0; index < pinnedItemIdsInOrder.Count; index++)
        {
            var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText =
                """
                UPDATE pinned_items
                SET sort_order = $sortOrder
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$sortOrder", index);
            command.Parameters.AddWithValue("$id", pinnedItemIdsInOrder[index]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static Guid? TryParseGuid(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Guid.TryParse(reader.GetString(ordinal), out var guid)
            ? guid
            : null;
    }
}
