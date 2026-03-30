using System.IO;
using Petrichor.Core.Domain;
using Petrichor.Core.Playback;

namespace Petrichor.Media.Playback;

public sealed class PlaybackOrchestrator : IDisposable
{
    private readonly IPlaybackBackend backend;
    private readonly PlaybackSessionService sessionService;
    private readonly PlaybackQueueController queueController;

    public PlaybackOrchestrator(IPlaybackBackend backend, PlaybackSessionService sessionService)
    {
        this.backend = backend;
        this.sessionService = sessionService;
        queueController = sessionService.QueueController;

        backend.PlaybackEnded += HandlePlaybackEnded;
        backend.PlaybackFailed += HandlePlaybackFailed;
    }

    public event EventHandler<PlaybackStatusSnapshot>? StatusChanged;

    public TrackReference? CurrentTrack { get; private set; }
    public string BackendDisplayName => backend.DisplayName;
    public AudioEngineCandidate BackendCandidate => backend.Candidate;
    public AudioBackendCapabilities BackendCapabilities => backend.Capabilities;
    public IPlaybackEffects Effects => backend.Effects;

    public PlaybackTransportState State => backend.State;

    public TimeSpan Position => backend.Position;

    public TimeSpan? Duration => backend.Duration;

    public double Volume => backend.Volume;

    public PlaybackQueueController QueueController => queueController;

    public async Task<bool> PlayAsync(TrackReference track, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(track.Path))
        {
            return false;
        }

        await backend.LoadAsync(track.Path, cancellationToken);
        backend.Play();
        CurrentTrack = track;
        PublishStatus();
        return true;
    }

    public async Task<bool> PlayQueueTrackAsync(int index, CancellationToken cancellationToken = default)
    {
        var track = queueController.PlayFromQueue(index);
        return track is not null && await PlayAsync(track, cancellationToken);
    }

    public async Task<bool> StartQueueAsync(
        IEnumerable<TrackReference> tracks,
        TrackReference startTrack,
        QueueSource source = QueueSource.Library,
        string? sourceIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        var queue = tracks.ToArray();
        queueController.SetQueue(queue, source, sourceIdentifier, startTrack);
        return await PlayAsync(startTrack, cancellationToken);
    }

    public void Pause()
    {
        backend.Pause();
        PublishStatus();
    }

    public void Resume()
    {
        backend.Play();
        PublishStatus();
    }

    public void Stop()
    {
        backend.Stop();
        PublishStatus();
    }

    public void Seek(TimeSpan position)
    {
        backend.Seek(position);
        PublishStatus();
    }

    public void SetVolume(double volume)
    {
        backend.SetVolume(volume);
        PublishStatus();
    }

    public void TogglePlayPause()
    {
        if (backend.State == PlaybackTransportState.Playing)
        {
            Pause();
        }
        else
        {
            Resume();
        }
    }

    public void ToggleShuffle()
    {
        queueController.ToggleShuffle();
        PublishStatus();
    }

    public void ToggleRepeatMode()
    {
        queueController.ToggleRepeatMode();
        PublishStatus();
    }

    public async Task<bool> PlayNextAsync(CancellationToken cancellationToken = default)
    {
        var nextTrack = queueController.GetNextTrack();
        return nextTrack is not null && await PlayAsync(nextTrack, cancellationToken);
    }

    public async Task<bool> PlayPreviousAsync(bool restartThresholdReached, CancellationToken cancellationToken = default)
    {
        var previousTrack = queueController.GetPreviousTrack(restartThresholdReached);
        return previousTrack is not null && await PlayAsync(previousTrack, cancellationToken);
    }

    public async Task<PlaybackRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var restoreResult = await sessionService.RestoreAsync(cancellationToken);
        CurrentTrack = restoreResult.CurrentTrack;

        if (restoreResult.CurrentTrack is { } restoredTrack && File.Exists(restoredTrack.Path))
        {
            await backend.LoadAsync(restoredTrack.Path, cancellationToken);
            if (restoreResult.SavedState is not null)
            {
                backend.Seek(TimeSpan.FromSeconds(restoreResult.SavedState.PlaybackPositionSeconds));
                backend.SetVolume(restoreResult.SavedState.Volume);
            }
        }

        PublishStatus();
        return restoreResult;
    }

    public Task SaveSessionAsync(bool isQueueVisible, string appVersion, CancellationToken cancellationToken = default)
    {
        return sessionService.SaveAsync(
            playbackPositionSeconds: backend.Position.TotalSeconds,
            volume: (float)backend.Volume,
            isMuted: backend.Volume <= 0.001,
            isQueueVisible: isQueueVisible,
            appVersion: appVersion,
            cancellationToken: cancellationToken);
    }

    private async void HandlePlaybackEnded(object? sender, EventArgs e)
    {
        var nextTrack = queueController.HandleTrackCompletion();
        if (nextTrack is not null)
        {
            await PlayAsync(nextTrack);
            return;
        }

        CurrentTrack = null;
        PublishStatus();
    }

    private void HandlePlaybackFailed(object? sender, Exception exception)
    {
        PublishStatus();
    }

    private void PublishStatus()
    {
        StatusChanged?.Invoke(this, new PlaybackStatusSnapshot(
            CurrentTrack: CurrentTrack,
            State: backend.State,
            Position: backend.Position,
            Duration: backend.Duration,
            Volume: backend.Volume));
    }

    public void Dispose()
    {
        backend.PlaybackEnded -= HandlePlaybackEnded;
        backend.PlaybackFailed -= HandlePlaybackFailed;
        backend.Dispose();
    }
}
