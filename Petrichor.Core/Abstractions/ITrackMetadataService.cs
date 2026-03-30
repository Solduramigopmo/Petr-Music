using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface ITrackMetadataService
{
    Task<TrackReference> ExtractAsync(string filePath, long? preferredTrackId = null, CancellationToken cancellationToken = default);
}
