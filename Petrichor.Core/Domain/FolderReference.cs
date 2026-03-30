namespace Petrichor.Core.Domain;

public sealed record FolderReference(
    long? Id,
    string Path,
    string Name,
    int TrackCount);
