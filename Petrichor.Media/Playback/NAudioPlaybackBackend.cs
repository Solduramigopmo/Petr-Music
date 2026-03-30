using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Petrichor.Media.Playback;

public sealed class NAudioPlaybackBackend : IPlaybackBackend
{
    private const double DefaultVolume = 0.7;

    private readonly object syncRoot = new();
    private readonly NAudioPlaybackEffects playbackEffects = new();
    private IWavePlayer? outputDevice;
    private AudioFileReader? audioFileReader;
    private EqualizerSampleProvider? equalizerSampleProvider;
    private PlaybackTransportState state = PlaybackTransportState.Idle;
    private double currentVolume = DefaultVolume;
    private bool isDisposed;
    private bool isStopRequested;
    private bool isTrackLoaded;

    public NAudioPlaybackBackend()
    {
        outputDevice = CreateOutputDevice();
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<Exception>? PlaybackFailed;

    public string DisplayName => "NAudio";
    public AudioEngineCandidate Candidate => AudioEngineCandidate.NAudio;
    public AudioBackendCapabilities Capabilities => new(
        SupportsEqualizer: true,
        SupportsDspEffects: true,
        SupportsGaplessPlayback: false,
        SupportsReplayGain: true,
        SupportsPitchControl: false);

    public IPlaybackEffects Effects => playbackEffects;
    public PlaybackTransportState State => state;

    public TimeSpan Position
    {
        get
        {
            lock (syncRoot)
            {
                return audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan? Duration
    {
        get
        {
            lock (syncRoot)
            {
                return audioFileReader?.TotalTime;
            }
        }
    }

    public double Volume
    {
        get
        {
            lock (syncRoot)
            {
                return currentVolume;
            }
        }
    }

    public Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ThrowIfDisposed();

            try
            {
                ResetPlaybackState();

                var reader = new AudioFileReader(path);
                reader.Volume = (float)currentVolume;
                var equalizer = new EqualizerSampleProvider(reader);
                var loudness = new LoudnessSampleProvider(equalizer);
                var replayGainDb = EstimateReplayGainDb(path);
                playbackEffects.Attach(equalizer, loudness, replayGainDb);

                var equalizedWaveProvider = new SampleToWaveProvider(loudness);
                var device = outputDevice ??= CreateOutputDevice();

                device.Init(equalizedWaveProvider);
                audioFileReader = reader;
                equalizerSampleProvider = equalizer;
                isTrackLoaded = true;
                state = PlaybackTransportState.Stopped;
            }
            catch (Exception exception)
            {
                ResetPlaybackState();
                state = PlaybackTransportState.Stopped;
                PlaybackFailed?.Invoke(this, exception);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public void Play()
    {
        lock (syncRoot)
        {
            if (!isTrackLoaded)
            {
                return;
            }

            outputDevice?.Play();
            state = PlaybackTransportState.Playing;
            isStopRequested = false;
        }
    }

    public void Pause()
    {
        lock (syncRoot)
        {
            if (!isTrackLoaded)
            {
                return;
            }

            outputDevice?.Pause();
            state = PlaybackTransportState.Paused;
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            if (!isTrackLoaded)
            {
                state = PlaybackTransportState.Stopped;
                return;
            }

            isStopRequested = true;
            outputDevice?.Stop();

            if (audioFileReader is not null)
            {
                audioFileReader.CurrentTime = TimeSpan.Zero;
            }

            state = PlaybackTransportState.Stopped;
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (syncRoot)
        {
            if (audioFileReader is null)
            {
                return;
            }

            var duration = audioFileReader.TotalTime;
            var clampedPosition = position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > duration
                    ? duration
                    : position;

            audioFileReader.CurrentTime = clampedPosition;
        }
    }

    public void SetVolume(double volume)
    {
        lock (syncRoot)
        {
            currentVolume = Math.Clamp(volume, 0, 1);

            if (audioFileReader is null)
            {
                return;
            }

            audioFileReader.Volume = (float)currentVolume;
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            ResetPlaybackState();
            outputDevice?.Dispose();
            outputDevice = null;
        }
    }

    private IWavePlayer CreateOutputDevice()
    {
        var device = new WaveOutEvent();
        device.PlaybackStopped += HandlePlaybackStopped;
        return device;
    }

    private void HandlePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Exception? playbackFailure = null;
        var shouldRaiseEnded = false;

        lock (syncRoot)
        {
            if (e.Exception is not null)
            {
                state = PlaybackTransportState.Stopped;
                playbackFailure = e.Exception;
            }
            else if (isTrackLoaded && !isStopRequested && audioFileReader is not null)
            {
                var remaining = audioFileReader.TotalTime - audioFileReader.CurrentTime;
                if (remaining <= TimeSpan.FromMilliseconds(125))
                {
                    state = PlaybackTransportState.Stopped;
                    shouldRaiseEnded = true;
                }
            }

            isStopRequested = false;
        }

        if (playbackFailure is not null)
        {
            PlaybackFailed?.Invoke(this, playbackFailure);
            return;
        }

        if (shouldRaiseEnded)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetPlaybackState()
    {
        isStopRequested = true;

        if (outputDevice is not null)
        {
            outputDevice.Stop();
        }

        audioFileReader?.Dispose();
        audioFileReader = null;
        equalizerSampleProvider = null;
        isTrackLoaded = false;
    }

    private static double EstimateReplayGainDb(string path)
    {
        try
        {
            using var analyzer = new AudioFileReader(path);
            var buffer = new float[4096];
            var samplesToScan = analyzer.WaveFormat.SampleRate * Math.Max(1, analyzer.WaveFormat.Channels) * 45;
            long samplesReadTotal = 0;
            double sumSquares = 0;
            var peak = 0f;

            while (samplesReadTotal < samplesToScan)
            {
                var requested = (int)Math.Min(buffer.Length, samplesToScan - samplesReadTotal);
                var samplesRead = analyzer.Read(buffer, 0, requested);
                if (samplesRead <= 0)
                {
                    break;
                }

                samplesReadTotal += samplesRead;

                for (var index = 0; index < samplesRead; index++)
                {
                    var sample = buffer[index];
                    var abs = Math.Abs(sample);
                    peak = Math.Max(peak, abs);
                    sumSquares += sample * sample;
                }
            }

            if (samplesReadTotal <= 0)
            {
                return 0;
            }

            var rms = Math.Sqrt(sumSquares / samplesReadTotal);
            var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-9));
            var targetDb = -18.0;
            var estimatedGainDb = targetDb - rmsDb;

            if (peak > 0)
            {
                var peakDb = 20 * Math.Log10(peak);
                var peakSafeGainDb = -1.0 - peakDb;
                estimatedGainDb = Math.Min(estimatedGainDb, peakSafeGainDb);
            }

            // Safety policy: replay gain may attenuate tracks, but never boost them.
            return Math.Clamp(estimatedGainDb, -12.0, 0.0);
        }
        catch
        {
            return 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
