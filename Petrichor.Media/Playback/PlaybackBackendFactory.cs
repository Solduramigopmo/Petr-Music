namespace Petrichor.Media.Playback;

public sealed class PlaybackBackendFactory
{
    public IPlaybackBackend Create(AudioEngineCandidate preferredCandidate = AudioEngineCandidate.Undecided)
    {
        return preferredCandidate switch
        {
            AudioEngineCandidate.ManagedBass => CreateManagedBassFallback(),
            AudioEngineCandidate.NAudio => CreateNaudioBackend(),
            _ => new WpfMediaPlayerBackend()
        };
    }

    private static IPlaybackBackend CreateManagedBassFallback()
    {
        return new WpfMediaPlayerBackend(
            displayName: "WPF MediaPlayer (ManagedBass planned)",
            candidate: AudioEngineCandidate.ManagedBass);
    }

    private static IPlaybackBackend CreateNaudioBackend()
    {
        try
        {
            return new NAudioPlaybackBackend();
        }
        catch
        {
            return new WpfMediaPlayerBackend(
                displayName: "WPF MediaPlayer (NAudio fallback)",
                candidate: AudioEngineCandidate.NAudio);
        }
    }
}
