namespace Petrichor.Core.Domain;

public sealed record LibraryImportResult(
    FolderReference Folder,
    int FilesDiscovered,
    int TracksImported,
    IReadOnlyList<TrackReference> ImportedTracks);
