using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;

namespace Petrichor.Data.Services;

public sealed class LibraryImportService : ILibraryImportService
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".alac"
    ];

    private readonly IFolderRepository folderRepository;
    private readonly ITrackRepository trackRepository;
    private readonly ITrackMetadataService trackMetadataService;

    public LibraryImportService(
        IFolderRepository folderRepository,
        ITrackRepository trackRepository,
        ITrackMetadataService trackMetadataService)
    {
        this.folderRepository = folderRepository;
        this.trackRepository = trackRepository;
        this.trackMetadataService = trackMetadataService;
    }

    public async Task<LibraryImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedFolderPath = Path.GetFullPath(folderPath);
        var discoveredFiles = Directory
            .EnumerateFiles(normalizedFolderPath, "*.*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var folder = new FolderReference(
            Id: null,
            Path: normalizedFolderPath,
            Name: Path.GetFileName(normalizedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TrackCount: discoveredFiles.Length);

        await folderRepository.UpsertAsync(folder, cancellationToken);
        var savedFolder = await folderRepository.GetByPathAsync(normalizedFolderPath, cancellationToken) ?? folder;

        var importedTracks = new List<TrackReference>(discoveredFiles.Length);
        long generatedIdBase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (var index = 0; index < discoveredFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = discoveredFiles[index];
            var track = await trackMetadataService.ExtractAsync(
                filePath,
                preferredTrackId: generatedIdBase + index,
                cancellationToken: cancellationToken);

            await trackRepository.UpsertAsync(track, savedFolder.Id, cancellationToken);
            importedTracks.Add(track);
        }

        if (savedFolder.Id is long folderId)
        {
            await trackRepository.RemoveMissingTracksForFolderAsync(folderId, discoveredFiles, cancellationToken);
        }

        var refreshedFolder = savedFolder with { TrackCount = importedTracks.Count };
        await folderRepository.UpsertAsync(refreshedFolder, cancellationToken);

        return new LibraryImportResult(
            Folder: refreshedFolder,
            FilesDiscovered: discoveredFiles.Length,
            TracksImported: importedTracks.Count,
            ImportedTracks: importedTracks);
    }

    public Task<IReadOnlyList<TrackReference>> GetLibraryTracksAsync(CancellationToken cancellationToken = default)
    {
        return trackRepository.GetAllAsync(cancellationToken);
    }

    public Task<IReadOnlyList<FolderReference>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        return folderRepository.GetAllAsync(cancellationToken);
    }
}
