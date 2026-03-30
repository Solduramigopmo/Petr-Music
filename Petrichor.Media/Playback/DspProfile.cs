namespace Petrichor.Media.Playback;

public sealed record DspProfile(
    bool LoudnessEnabled,
    bool ReplayGainEnabled,
    double LoudnessGainDb)
{
    public static DspProfile Default { get; } = new(
        LoudnessEnabled: false,
        ReplayGainEnabled: false,
        LoudnessGainDb: 3.0);
}
