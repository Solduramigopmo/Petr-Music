using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface ITrackRepository
{
    Task<IReadOnlyList<TrackReference>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackReference>> GetByIdsAsync(IEnumerable<long> trackIds, CancellationToken cancellationToken = default);
    Task<TrackReference?> GetByIdAsync(long trackId, CancellationToken cancellationToken = default);
    Task<TrackReference?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackReference>> GetByFolderPathAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackReference>> GetBySmartCriteriaAsync(string smartCriteriaJson, CancellationToken cancellationToken = default);
    Task RemoveMissingTracksForFolderAsync(long folderId, IReadOnlyList<string> discoveredPaths, CancellationToken cancellationToken = default);
    Task UpsertAsync(TrackReference track, long? folderId = null, CancellationToken cancellationToken = default);
}
