using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using File = TagLib.File;

namespace Petrichor.Data.Services;

public sealed class TagLibTrackMetadataService : ITrackMetadataService
{
    public Task<TrackReference> ExtractAsync(string filePath, long? preferredTrackId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var file = File.Create(filePath);

            var title = string.IsNullOrWhiteSpace(file.Tag.Title)
                ? Path.GetFileNameWithoutExtension(filePath)
                : file.Tag.Title;

            var artist = file.Tag.Performers.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            var album = string.IsNullOrWhiteSpace(file.Tag.Album) ? null : file.Tag.Album;
            var genre = file.Tag.Genres.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            var year = file.Tag.Year > 0 ? file.Tag.Year.ToString() : null;
            var duration = file.Properties.Duration.TotalSeconds > 0
                ? file.Properties.Duration.TotalSeconds
                : 0;

            return Task.FromResult(new TrackReference(
                TrackId: preferredTrackId,
                Path: filePath,
                Title: title,
                Artist: artist,
                Album: album,
                DurationSeconds: duration,
                Genre: genre,
                Year: year));
        }
        catch
        {
            return Task.FromResult(new TrackReference(
                TrackId: preferredTrackId,
                Path: filePath,
                Title: Path.GetFileNameWithoutExtension(filePath),
                Artist: null,
                Album: null,
                DurationSeconds: 0,
                Genre: null,
                Year: null));
        }
    }
}
