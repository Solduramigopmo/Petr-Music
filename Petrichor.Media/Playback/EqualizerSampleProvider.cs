using NAudio.Dsp;
using NAudio.Wave;

namespace Petrichor.Media.Playback;

internal sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly object syncRoot = new();
    private BiQuadFilter[][] filters = [];
    private EqualizerProfile profile = EqualizerProfile.Flat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        this.source = source;
        WaveFormat = source.WaveFormat;
        RebuildFilters();
    }

    public WaveFormat WaveFormat { get; }

    public EqualizerProfile Profile
    {
        get
        {
            lock (syncRoot)
            {
                return profile;
            }
        }
    }

    public void UpdateProfile(EqualizerProfile updatedProfile)
    {
        lock (syncRoot)
        {
            profile = updatedProfile;
            RebuildFilters();
        }
    }

    public void Reset()
    {
        UpdateProfile(EqualizerProfile.Flat);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        if (samplesRead <= 0)
        {
            return samplesRead;
        }

        BiQuadFilter[][] localFilters;
        EqualizerProfile localProfile;

        lock (syncRoot)
        {
            localFilters = filters;
            localProfile = profile;
        }

        if (!localProfile.IsEnabled || localFilters.Length == 0)
        {
            return samplesRead;
        }

        var channels = Math.Max(1, WaveFormat.Channels);
        var preamp = (float)Math.Pow(10, localProfile.PreampDb / 20d);

        for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
        {
            var channel = sampleIndex % channels;
            var value = buffer[offset + sampleIndex] * preamp;

            foreach (var filter in localFilters[channel])
            {
                value = filter.Transform(value);
            }

            buffer[offset + sampleIndex] = Math.Clamp(value, -1f, 1f);
        }

        return samplesRead;
    }

    private void RebuildFilters()
    {
        var channels = Math.Max(1, WaveFormat.Channels);
        var sampleRate = WaveFormat.SampleRate;
        var bands = profile.Bands;

        if (!profile.IsEnabled || bands.Count == 0)
        {
            filters = [];
            return;
        }

        filters = new BiQuadFilter[channels][];

        for (var channel = 0; channel < channels; channel++)
        {
            filters[channel] = bands
                .Select(band => BiQuadFilter.PeakingEQ(
                    sampleRate,
                    (float)Math.Clamp(band.FrequencyHz, 20, 20000),
                    (float)Math.Max(0.1, band.Q),
                    (float)band.GainDb))
                .ToArray();
        }
    }
}
