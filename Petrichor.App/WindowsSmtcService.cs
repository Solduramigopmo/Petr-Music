using System.IO;
using Petrichor.Core.Domain;
using Petrichor.Media.Playback;
using Windows.Media;
using Windows.Storage.Streams;
using MediaPlaybackType = Windows.Media.MediaPlaybackType;

namespace Petrichor.App;

internal sealed class WindowsSmtcService : IDisposable
{
    private readonly Windows.Media.Playback.MediaPlayer mediaPlayer;
    private readonly SystemMediaTransportControls controls;

    private string? lastTrackPath;
    private bool disposed;

    private WindowsSmtcService(Windows.Media.Playback.MediaPlayer mediaPlayer)
    {
        this.mediaPlayer = mediaPlayer;
        controls = mediaPlayer.SystemMediaTransportControls;

        controls.IsEnabled = true;
        controls.IsPlayEnabled = true;
        controls.IsPauseEnabled = true;
        controls.IsNextEnabled = true;
        controls.IsPreviousEnabled = true;
        controls.IsStopEnabled = true;

        controls.ButtonPressed += Controls_ButtonPressed;
    }

    public event EventHandler<SystemMediaTransportControlsButton>? ButtonPressed;

    public static WindowsSmtcService? Create()
    {
        try
        {
            var mediaPlayer = new Windows.Media.Playback.MediaPlayer();
            mediaPlayer.CommandManager.IsEnabled = false;
            return new WindowsSmtcService(mediaPlayer);
        }
        catch
        {
            return null;
        }
    }

    public void UpdateSession(
        TrackReference? track,
        PlaybackTransportState state,
        TimeSpan position,
        TimeSpan duration,
        bool canGoNext,
        bool canGoPrevious)
    {
        if (disposed)
        {
            return;
        }

        controls.IsPlayEnabled = track is not null;
        controls.IsPauseEnabled = track is not null;
        controls.IsStopEnabled = track is not null;
        controls.IsNextEnabled = canGoNext;
        controls.IsPreviousEnabled = canGoPrevious;

        controls.PlaybackStatus = state switch
        {
            PlaybackTransportState.Playing => MediaPlaybackStatus.Playing,
            PlaybackTransportState.Paused => MediaPlaybackStatus.Paused,
            PlaybackTransportState.Stopped => MediaPlaybackStatus.Stopped,
            _ => track is null ? MediaPlaybackStatus.Closed : MediaPlaybackStatus.Stopped
        };

        UpdateMetadata(track);
        UpdateTimeline(position, duration);
    }

    public void UpdateTimeline(TimeSpan position, TimeSpan duration)
    {
        if (disposed)
        {
            return;
        }

        var clampedPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        var clampedDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var effectiveDuration = clampedDuration == TimeSpan.Zero
            ? clampedPosition
            : clampedDuration;

        var timeline = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            Position = clampedPosition,
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = effectiveDuration,
            EndTime = effectiveDuration
        };

        controls.UpdateTimelineProperties(timeline);
    }

    private void UpdateMetadata(TrackReference? track)
    {
        if (track is null)
        {
            lastTrackPath = null;
            var updater = controls.DisplayUpdater;
            updater.ClearAll();
            updater.Type = MediaPlaybackType.Music;
            updater.Update();
            return;
        }

        if (string.Equals(lastTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lastTrackPath = track.Path;

        var displayUpdater = controls.DisplayUpdater;
        displayUpdater.Type = MediaPlaybackType.Music;
        displayUpdater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title)
            ? Path.GetFileNameWithoutExtension(track.Path)
            : track.Title;
        displayUpdater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist)
            ? "Unknown Artist"
            : track.Artist;
        displayUpdater.MusicProperties.AlbumTitle = string.IsNullOrWhiteSpace(track.Album)
            ? "Unknown Album"
            : track.Album;

        displayUpdater.Thumbnail = TryCreateThumbnail(track.Path);
        displayUpdater.Update();
    }

    private static RandomAccessStreamReference? TryCreateThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var uri = new Uri(path, UriKind.Absolute);
            return RandomAccessStreamReference.CreateFromUri(uri);
        }
        catch
        {
            return null;
        }
    }

    private void Controls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if (disposed)
        {
            return;
        }

        ButtonPressed?.Invoke(this, args.Button);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        controls.ButtonPressed -= Controls_ButtonPressed;
        mediaPlayer.Dispose();
    }
}
