namespace Petrichor.Media.Playback;

public sealed class NoOpPlaybackEffects : IPlaybackEffects
{
    private EqualizerProfile equalizerProfile = EqualizerProfile.Flat;
    private DspProfile dspProfile = DspProfile.Default;

    public bool IsAvailable => false;

    public EqualizerProfile EqualizerProfile => equalizerProfile;
    public DspProfile DspProfile => dspProfile;

    public void ApplyEqualizer(EqualizerProfile profile)
    {
        equalizerProfile = profile;
    }

    public void ApplyDspProfile(DspProfile profile)
    {
        dspProfile = profile;
    }

    public void Reset()
    {
        equalizerProfile = EqualizerProfile.Flat;
        dspProfile = DspProfile.Default;
    }
}
