namespace Petrichor.Media.Playback;

public sealed record NamedEqualizerPreset(
    string Name,
    EqualizerProfile Profile,
    bool IsBuiltIn);
