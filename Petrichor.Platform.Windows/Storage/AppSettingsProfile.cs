namespace Petrichor.Platform.Windows.Storage;

public sealed record AppSettingsProfile
{
    public bool StartMinimizedToTray { get; init; } = false;
    public bool RestoreLastSession { get; init; } = true;
    public bool AutoPlayAfterRestore { get; init; } = false;
    public bool MinimizeToTrayOnClose { get; init; } = true;
    public bool ShowTrackNotifications { get; init; } = true;

    public string StartupSection { get; init; } = "Library";
    public bool RememberLastSection { get; init; } = false;
    public string LastActiveSection { get; init; } = "Library";

    public string LibrarySortMode { get; init; } = "title";
    public string LibraryFilterMode { get; init; } = "all";
    public bool AutoScanLibraryEnabled { get; init; } = true;

    public bool OnlineLyricsEnabled { get; init; } = false;
    public bool LastFmScrobblingEnabled { get; init; } = true;
    public string LastFmUsername { get; init; } = string.Empty;

    public static AppSettingsProfile Default { get; } = new();
}
