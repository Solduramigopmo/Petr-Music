namespace Petrichor.Core.Domain;

public sealed record PinnedItemReference(
    long? Id,
    string ItemType,
    string? FilterType,
    string? FilterValue,
    Guid? EntityId,
    Guid? PlaylistId,
    string DisplayName,
    string IconName,
    int SortOrder);
