namespace Petrichor.Media.Playback;

public sealed record AudioBackendCapabilities(
    bool SupportsEqualizer,
    bool SupportsDspEffects,
    bool SupportsGaplessPlayback,
    bool SupportsReplayGain,
    bool SupportsPitchControl)
{
    public static AudioBackendCapabilities Basic { get; } = new(
        SupportsEqualizer: false,
        SupportsDspEffects: false,
        SupportsGaplessPlayback: false,
        SupportsReplayGain: false,
        SupportsPitchControl: false);
}
