using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface ILibraryImportService
{
    Task<LibraryImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackReference>> GetLibraryTracksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FolderReference>> GetFoldersAsync(CancellationToken cancellationToken = default);
}
