using NAudio.Wave;

namespace Petrichor.Media.Playback;

internal sealed class LoudnessSampleProvider : ISampleProvider
{
    private const double MaxSafeTotalGainDb = 4.0;
    private const double TargetPeakDb = -1.0;

    private readonly ISampleProvider source;
    private readonly object syncRoot = new();
    private DspProfile profile = DspProfile.Default;
    private double replayGainDb;

    public LoudnessSampleProvider(ISampleProvider source)
    {
        this.source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateProfile(DspProfile profile, double replayGainDb)
    {
        lock (syncRoot)
        {
            this.profile = profile;
            this.replayGainDb = Math.Min(0, replayGainDb);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        if (samplesRead <= 0)
        {
            return samplesRead;
        }

        DspProfile localProfile;
        double localReplayGain;
        lock (syncRoot)
        {
            localProfile = profile;
            localReplayGain = replayGainDb;
        }

        var totalGainDb =
            (localProfile.LoudnessEnabled ? localProfile.LoudnessGainDb : 0) +
            (localProfile.ReplayGainEnabled ? localReplayGain : 0);

        if (Math.Abs(totalGainDb) <= 0.01)
        {
            return samplesRead;
        }

        totalGainDb = Math.Clamp(totalGainDb, -12.0, MaxSafeTotalGainDb);

        var sourcePeak = 0f;
        for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
        {
            sourcePeak = Math.Max(sourcePeak, Math.Abs(buffer[offset + sampleIndex]));
        }

        if (sourcePeak > 0)
        {
            var sourcePeakDb = 20 * Math.Log10(sourcePeak);
            var peakHeadroomGainDb = TargetPeakDb - sourcePeakDb;
            totalGainDb = Math.Min(totalGainDb, peakHeadroomGainDb);
        }

        var linearGain = (float)Math.Pow(10, totalGainDb / 20d);
        for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
        {
            var value = buffer[offset + sampleIndex] * linearGain;

            // Keep aggressive gain settings musical by softly taming peaks.
            if (Math.Abs(value) > 0.98f)
            {
                value = (float)Math.Tanh(value);
            }

            buffer[offset + sampleIndex] = Math.Clamp(value, -1f, 1f);
        }

        return samplesRead;
    }
}
