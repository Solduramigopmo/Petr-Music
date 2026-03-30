using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Core.Playback;
using Petrichor.Data.Persistence;

namespace Petrichor.Media.Playback;

public sealed class PlaybackSessionService
{
    private readonly PlaybackStateJsonStore stateStore;
    private readonly ITrackRepository trackRepository;
    private readonly IPlaylistTrackRepository playlistTrackRepository;
    private readonly PlaybackQueueController queueController;

    public PlaybackSessionService(
        PlaybackStateJsonStore stateStore,
        ITrackRepository trackRepository,
        IPlaylistTrackRepository playlistTrackRepository,
        PlaybackQueueController queueController)
    {
        this.stateStore = stateStore;
        this.trackRepository = trackRepository;
        this.playlistTrackRepository = playlistTrackRepository;
        this.queueController = queueController;
    }

    public PlaybackQueueController QueueController => queueController;

    public async Task SaveAsync(
        double playbackPositionSeconds,
        float volume,
        bool isMuted,
        bool isQueueVisible,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var queueState = queueController.CaptureState();
        var snapshot = new PlaybackSessionSnapshot(
            CurrentTrack: queueController.CurrentTrack,
            PlaybackPositionSeconds: playbackPositionSeconds,
            Queue: queueState.Queue,
            CurrentQueueIndex: queueState.CurrentQueueIndex,
            QueueSource: queueState.QueueSource,
            SourceIdentifier: queueState.SourceIdentifier,
            Volume: volume,
            IsMuted: isMuted,
            ShuffleEnabled: queueState.ShuffleEnabled,
            RepeatMode: queueState.RepeatMode,
            IsQueueVisible: isQueueVisible,
            SavedAtUtc: DateTimeOffset.UtcNow);

        var record = PlaybackStateRecord.FromSnapshot(snapshot, appVersion);
        await stateStore.SaveAsync(record, cancellationToken);
    }

    public async Task<PlaybackRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var savedState = await stateStore.LoadAsync(cancellationToken);
        if (savedState is null)
        {
            queueController.Clear();
            return new PlaybackRestoreResult(null, null, Array.Empty<TrackReference>());
        }

        IReadOnlyList<TrackReference> restoredQueue;

        if (savedState.QueueSource == QueueSource.Playlist && Guid.TryParse(savedState.SourceIdentifier, out var playlistId))
        {
            restoredQueue = await playlistTrackRepository.GetTracksForPlaylistAsync(playlistId, cancellationToken);
        }
        else if (savedState.QueueSource == QueueSource.Folder && !string.IsNullOrWhiteSpace(savedState.SourceIdentifier))
        {
            restoredQueue = await trackRepository.GetByFolderPathAsync(savedState.SourceIdentifier, cancellationToken);
        }
        else if (savedState.QueueTrackIds.Count > 0)
        {
            restoredQueue = await trackRepository.GetByIdsAsync(savedState.QueueTrackIds, cancellationToken);
        }
        else
        {
            var fallbackTracks = new List<TrackReference>();
            foreach (var path in savedState.QueueTrackPaths)
            {
                var track = await trackRepository.GetByPathAsync(path, cancellationToken);
                if (track is not null)
                {
                    fallbackTracks.Add(track);
                }
            }

            restoredQueue = fallbackTracks;
        }

        TrackReference? currentTrack = null;
        if (savedState.CurrentTrackId.HasValue)
        {
            currentTrack = restoredQueue.FirstOrDefault(track => track.TrackId == savedState.CurrentTrackId)
                ?? await trackRepository.GetByIdAsync(savedState.CurrentTrackId.Value, cancellationToken);
        }

        if (currentTrack is null && !string.IsNullOrWhiteSpace(savedState.CurrentTrackPath))
        {
            currentTrack = restoredQueue.FirstOrDefault(track =>
                    string.Equals(track.Path, savedState.CurrentTrackPath, StringComparison.OrdinalIgnoreCase))
                ?? await trackRepository.GetByPathAsync(savedState.CurrentTrackPath, cancellationToken);
        }

        queueController.Restore(new PlaybackQueueState(
            Queue: restoredQueue,
            CurrentQueueIndex: savedState.CurrentQueueIndex,
            QueueSource: savedState.QueueSource,
            ShuffleEnabled: savedState.ShuffleEnabled,
            RepeatMode: savedState.RepeatMode,
            SourceIdentifier: savedState.SourceIdentifier));

        return new PlaybackRestoreResult(savedState, currentTrack, restoredQueue);
    }
}
