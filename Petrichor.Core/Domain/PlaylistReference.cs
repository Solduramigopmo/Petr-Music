namespace Petrichor.Core.Domain;

public sealed record PlaylistReference(
    Guid Id,
    string Name,
    string Type,
    bool IsUserEditable,
    bool IsContentEditable,
    int TrackCount,
    int SortOrder,
    string? SmartCriteriaJson = null);
