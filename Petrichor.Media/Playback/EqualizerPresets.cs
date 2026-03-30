namespace Petrichor.Media.Playback;

public static class EqualizerPresets
{
    public const string FlatName = "Flat";
    public const string BassBoostName = "Bass Boost";
    public const string VocalName = "Vocal";
    public const string TrebleLiftName = "Treble Lift";

    public static EqualizerProfile Flat => EqualizerProfile.Flat;

    public static EqualizerProfile BassBoost { get; } = new(
        IsEnabled: true,
        PreampDb: -3.0,
        Bands:
        [
            new EqualizerBand(31.25, 8.0, 0.9),
            new EqualizerBand(62.5, 6.5, 0.9),
            new EqualizerBand(125, 4.5, 1.0),
            new EqualizerBand(250, 2.0, 1.0),
            new EqualizerBand(500, 0.0, 1.0),
            new EqualizerBand(1000, -1.0, 1.0),
            new EqualizerBand(2000, -2.5, 1.1),
            new EqualizerBand(4000, -3.0, 1.1),
            new EqualizerBand(8000, -2.0, 1.0),
            new EqualizerBand(16000, -1.0, 0.9)
        ]);

    public static EqualizerProfile Vocal { get; } = new(
        IsEnabled: true,
        PreampDb: -2.0,
        Bands:
        [
            new EqualizerBand(31.25, -4.0, 0.8),
            new EqualizerBand(62.5, -3.0, 0.8),
            new EqualizerBand(125, -2.0, 0.9),
            new EqualizerBand(250, -0.5, 1.0),
            new EqualizerBand(500, 1.5, 1.0),
            new EqualizerBand(1000, 3.5, 1.1),
            new EqualizerBand(2000, 5.0, 1.1),
            new EqualizerBand(4000, 3.5, 1.0),
            new EqualizerBand(8000, 1.0, 0.9),
            new EqualizerBand(16000, -0.5, 0.8)
        ]);

    public static EqualizerProfile TrebleLift { get; } = new(
        IsEnabled: true,
        PreampDb: -2.5,
        Bands:
        [
            new EqualizerBand(31.25, -3.5, 0.8),
            new EqualizerBand(62.5, -3.0, 0.8),
            new EqualizerBand(125, -2.0, 0.9),
            new EqualizerBand(250, -1.0, 0.9),
            new EqualizerBand(500, 0.0, 1.0),
            new EqualizerBand(1000, 1.5, 1.0),
            new EqualizerBand(2000, 3.5, 1.0),
            new EqualizerBand(4000, 5.0, 1.1),
            new EqualizerBand(8000, 6.0, 1.0),
            new EqualizerBand(16000, 6.5, 0.9)
        ]);

    public static IReadOnlyList<NamedEqualizerPreset> BuiltIn { get; } =
    [
        new NamedEqualizerPreset(FlatName, Flat, true),
        new NamedEqualizerPreset(BassBoostName, BassBoost, true),
        new NamedEqualizerPreset(VocalName, Vocal, true),
        new NamedEqualizerPreset(TrebleLiftName, TrebleLift, true)
    ];
}
