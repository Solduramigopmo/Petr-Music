using System.Windows.Media;

namespace Petrichor.Media.Playback;

public sealed class WpfMediaPlayerBackend : IPlaybackBackend
{
    private readonly MediaPlayer player = new();
    private readonly NoOpPlaybackEffects effects = new();
    private PlaybackTransportState state = PlaybackTransportState.Idle;
    private bool isDisposed;

    public WpfMediaPlayerBackend(
        string displayName = "WPF MediaPlayer",
        AudioEngineCandidate candidate = AudioEngineCandidate.Undecided)
    {
        DisplayName = displayName;
        Candidate = candidate;

        player.MediaEnded += (_, _) =>
        {
            state = PlaybackTransportState.Stopped;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };

        player.MediaFailed += (_, args) =>
        {
            state = PlaybackTransportState.Stopped;
            PlaybackFailed?.Invoke(this, args.ErrorException);
        };
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<Exception>? PlaybackFailed;

    public string DisplayName { get; }
    public AudioEngineCandidate Candidate { get; }
    public AudioBackendCapabilities Capabilities => AudioBackendCapabilities.Basic;
    public IPlaybackEffects Effects => effects;
    public PlaybackTransportState State => state;
    public TimeSpan Position => player.Position;
    public TimeSpan? Duration => player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : null;
    public double Volume => player.Volume;

    public Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        player.Open(new Uri(path, UriKind.Absolute));
        state = PlaybackTransportState.Stopped;
        return Task.CompletedTask;
    }

    public void Play()
    {
        player.Play();
        state = PlaybackTransportState.Playing;
    }

    public void Pause()
    {
        player.Pause();
        state = PlaybackTransportState.Paused;
    }

    public void Stop()
    {
        player.Stop();
        state = PlaybackTransportState.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        player.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    public void SetVolume(double volume)
    {
        player.Volume = Math.Clamp(volume, 0, 1);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        player.Close();
    }
}
