using System;
using System.IO;
using Petrichor.Data.Persistence;

namespace Petrichor.Platform.Windows.Storage;

public sealed class WindowsApplicationPaths
{
    public WindowsApplicationPaths(DatabaseProfile databaseProfile)
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ApplicationRoot = Path.Combine(appDataRoot, databaseProfile.ApplicationDirectoryName);
        DatabaseFilePath = Path.Combine(ApplicationRoot, databaseProfile.DatabaseFileName);
        ArtworkCacheDirectory = Path.Combine(ApplicationRoot, databaseProfile.ArtworkCacheDirectoryName);
        PlaybackStateFilePath = Path.Combine(ApplicationRoot, "playback-state.json");
        AppSettingsFilePath = Path.Combine(ApplicationRoot, "settings.json");
        EqualizerProfileFilePath = Path.Combine(ApplicationRoot, "equalizer-profile.json");
        DspProfileFilePath = Path.Combine(ApplicationRoot, "dsp-profile.json");
        EqualizerPresetsFilePath = Path.Combine(ApplicationRoot, "equalizer-presets.json");
        LastFmSessionFilePath = Path.Combine(ApplicationRoot, "lastfm-session.bin");
    }

    public string ApplicationRoot { get; }
    public string DatabaseFilePath { get; }
    public string ArtworkCacheDirectory { get; }
    public string PlaybackStateFilePath { get; }
    public string AppSettingsFilePath { get; }
    public string EqualizerProfileFilePath { get; }
    public string DspProfileFilePath { get; }
    public string EqualizerPresetsFilePath { get; }
    public string LastFmSessionFilePath { get; }
}
