namespace Petrichor.Media.Playback;

internal sealed class NAudioPlaybackEffects : IPlaybackEffects
{
    private const double MaxSafeLoudnessGainDb = 6.0;

    private EqualizerSampleProvider? equalizerSampleProvider;
    private LoudnessSampleProvider? loudnessSampleProvider;
    private EqualizerProfile equalizerProfile = EqualizerProfile.Flat;
    private DspProfile dspProfile = DspProfile.Default;
    private double replayGainDb;

    internal NAudioPlaybackEffects(EqualizerSampleProvider? equalizerSampleProvider = null)
    {
        this.equalizerSampleProvider = equalizerSampleProvider;
        if (equalizerSampleProvider is not null)
        {
            equalizerProfile = equalizerSampleProvider.Profile;
        }
    }

    public bool IsAvailable => true;

    public EqualizerProfile EqualizerProfile => equalizerProfile;
    public DspProfile DspProfile => dspProfile;

    public void Attach(EqualizerSampleProvider equalizerSampleProvider, LoudnessSampleProvider loudnessSampleProvider, double replayGainDb)
    {
        this.equalizerSampleProvider = equalizerSampleProvider;
        this.loudnessSampleProvider = loudnessSampleProvider;
        this.replayGainDb = Math.Min(0, replayGainDb);
        equalizerSampleProvider.UpdateProfile(equalizerProfile);
        loudnessSampleProvider.UpdateProfile(dspProfile, this.replayGainDb);
    }

    public void ApplyEqualizer(EqualizerProfile profile)
    {
        equalizerProfile = profile;
        equalizerSampleProvider?.UpdateProfile(profile);
    }

    public void ApplyDspProfile(DspProfile profile)
    {
        var normalizedProfile = profile with
        {
            LoudnessGainDb = Math.Clamp(profile.LoudnessGainDb, 0, MaxSafeLoudnessGainDb)
        };

        dspProfile = normalizedProfile;
        loudnessSampleProvider?.UpdateProfile(normalizedProfile, replayGainDb);
    }

    public void Reset()
    {
        ApplyEqualizer(EqualizerProfile.Flat);
        ApplyDspProfile(DspProfile.Default);
    }
}
