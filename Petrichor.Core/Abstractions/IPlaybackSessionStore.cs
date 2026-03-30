using System.Threading;
using System.Threading.Tasks;
using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface IPlaybackSessionStore
{
    Task SaveAsync(PlaybackSessionSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<PlaybackSessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
