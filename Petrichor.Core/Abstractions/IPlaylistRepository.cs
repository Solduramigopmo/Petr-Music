using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface IPlaylistRepository
{
    Task<IReadOnlyList<PlaylistReference>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PlaylistReference?> GetByIdAsync(Guid playlistId, CancellationToken cancellationToken = default);
    Task UpsertAsync(PlaylistReference playlist, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid playlistId, CancellationToken cancellationToken = default);
}
