using System;

namespace Petrichor.Core.Domain;

public sealed record PlaybackStateRecord
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public string? CurrentTrackPath { get; init; }
    public long? CurrentTrackId { get; init; }
    public double PlaybackPositionSeconds { get; init; }
    public double TrackDurationSeconds { get; init; }
    public bool IsQueueVisible { get; init; }
    public IReadOnlyList<string> QueueTrackPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> QueueTrackIds { get; init; } = Array.Empty<long>();
    public int CurrentQueueIndex { get; init; }
    public QueueSource QueueSource { get; init; } = QueueSource.Library;
    public string? SourceIdentifier { get; init; }
    public float Volume { get; init; }
    public bool IsMuted { get; init; }
    public bool ShuffleEnabled { get; init; }
    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; init; } = "1.0";

    public static PlaybackStateRecord FromSnapshot(PlaybackSessionSnapshot snapshot, string appVersion)
    {
        return new PlaybackStateRecord
        {
            CurrentTrackPath = snapshot.CurrentTrack?.Path,
            CurrentTrackId = snapshot.CurrentTrack?.TrackId,
            PlaybackPositionSeconds = snapshot.PlaybackPositionSeconds,
            TrackDurationSeconds = snapshot.CurrentTrack?.DurationSeconds ?? 0,
            IsQueueVisible = snapshot.IsQueueVisible,
            QueueTrackPaths = snapshot.Queue.Select(track => track.Path).ToArray(),
            QueueTrackIds = snapshot.Queue.Where(track => track.TrackId.HasValue).Select(track => track.TrackId!.Value).ToArray(),
            CurrentQueueIndex = snapshot.CurrentQueueIndex,
            QueueSource = snapshot.QueueSource,
            SourceIdentifier = snapshot.SourceIdentifier,
            Volume = snapshot.Volume,
            IsMuted = snapshot.IsMuted,
            ShuffleEnabled = snapshot.ShuffleEnabled,
            RepeatMode = snapshot.RepeatMode,
            SavedAtUtc = snapshot.SavedAtUtc,
            AppVersion = appVersion
        };
    }

    public PlaybackUiState? CreateUiState(TrackReference? currentTrack, byte[]? artworkData = null)
    {
        if (currentTrack is null)
        {
            return null;
        }

        return new PlaybackUiState(
            TrackTitle: currentTrack.Title,
            TrackArtist: currentTrack.Artist,
            TrackAlbum: currentTrack.Album,
            ArtworkData: artworkData,
            PlaybackPositionSeconds: PlaybackPositionSeconds,
            TrackDurationSeconds: TrackDurationSeconds,
            Volume: Volume,
            IsQueueVisible: IsQueueVisible);
    }
}
