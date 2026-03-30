namespace Petrichor.Media.Playback;

public interface IPlaybackBackend : IDisposable
{
    event EventHandler? PlaybackEnded;
    event EventHandler<Exception>? PlaybackFailed;

    string DisplayName { get; }
    AudioEngineCandidate Candidate { get; }
    AudioBackendCapabilities Capabilities { get; }
    IPlaybackEffects Effects { get; }
    PlaybackTransportState State { get; }
    TimeSpan Position { get; }
    TimeSpan? Duration { get; }
    double Volume { get; }

    Task LoadAsync(string path, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void SetVolume(double volume);
}
