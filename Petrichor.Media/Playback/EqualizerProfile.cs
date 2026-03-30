namespace Petrichor.Media.Playback;

public sealed record EqualizerProfile(
    bool IsEnabled,
    double PreampDb,
    IReadOnlyList<EqualizerBand> Bands)
{
    public static EqualizerProfile Flat { get; } = new(
        IsEnabled: false,
        PreampDb: 0,
        Bands:
        [
            new EqualizerBand(31.25, 0),
            new EqualizerBand(62.5, 0),
            new EqualizerBand(125, 0),
            new EqualizerBand(250, 0),
            new EqualizerBand(500, 0),
            new EqualizerBand(1000, 0),
            new EqualizerBand(2000, 0),
            new EqualizerBand(4000, 0),
            new EqualizerBand(8000, 0),
            new EqualizerBand(16000, 0)
        ]);
}
