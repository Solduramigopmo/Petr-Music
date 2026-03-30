namespace Petrichor.Media.Playback;

public interface IPlaybackEffects
{
    bool IsAvailable { get; }
    EqualizerProfile EqualizerProfile { get; }
    DspProfile DspProfile { get; }

    void ApplyEqualizer(EqualizerProfile profile);
    void ApplyDspProfile(DspProfile profile);
    void Reset();
}
