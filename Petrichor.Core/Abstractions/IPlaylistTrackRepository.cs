using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface IPlaylistTrackRepository
{
    Task<IReadOnlyList<TrackReference>> GetTracksForPlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default);
    Task ReplaceTracksAsync(Guid playlistId, IEnumerable<long> trackIds, CancellationToken cancellationToken = default);
}
