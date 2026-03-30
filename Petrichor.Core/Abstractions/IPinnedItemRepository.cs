using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface IPinnedItemRepository
{
    Task<IReadOnlyList<PinnedItemReference>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(PinnedItemReference item, CancellationToken cancellationToken = default);
    Task DeleteAsync(long pinnedItemId, CancellationToken cancellationToken = default);
    Task ReorderAsync(IReadOnlyList<long> pinnedItemIdsInOrder, CancellationToken cancellationToken = default);
}
