namespace Petrichor.Media.Playback;

public sealed record EqualizerBand(
    double FrequencyHz,
    double GainDb,
    double Q = 1.0);
