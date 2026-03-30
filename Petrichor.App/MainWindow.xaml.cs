using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Core.Playback;
using Petrichor.Data.Persistence;
using Petrichor.Data.Repositories;
using Petrichor.Data.Services;
using Petrichor.Media.Playback;
using Petrichor.Platform.Windows.Shell;
using Petrichor.Platform.Windows.Storage;
using Windows.Media;

namespace Petrichor.App;

public partial class MainWindow : Window
{
    private const string HomeSection = "Home";
    private const string LibrarySection = "Library";
    private const string PlaylistsSection = "Playlists";
    private const string FoldersSection = "Folders";
    private const string SettingsSection = "Settings";
    private const int WmHotkey = 0x0312;
    private const int HotkeyPlayPauseId = 1001;
    private const int HotkeyNextId = 1002;
    private const int HotkeyPreviousId = 1003;
    private const int VkMediaNextTrack = 0xB0;
    private const int VkMediaPreviousTrack = 0xB1;
    private const int VkMediaPlayPause = 0xB3;

    private readonly PlaybackOrchestrator playbackOrchestrator;
    private readonly ILibraryImportService libraryImportService;
    private readonly ITrackRepository trackRepository;
    private readonly IFolderRepository folderRepository;
    private readonly IPlaylistRepository playlistRepository;
    private readonly IPlaylistTrackRepository playlistTrackRepository;
    private readonly IPinnedItemRepository pinnedItemRepository;
    private readonly AppSettingsJsonStore appSettingsStore;
    private readonly EqualizerProfileJsonStore equalizerProfileStore;
    private readonly DspProfileJsonStore dspProfileStore;
    private readonly NamedEqualizerPresetJsonStore equalizerPresetStore;
    private readonly LibraryAutoScanService libraryAutoScanService;
    private readonly WindowsSmtcService? smtcService;
    private readonly LyricsService lyricsService;
    private readonly LastFmScrobbleService lastFmScrobbleService;
    private readonly DispatcherTimer positionTimer;
    private readonly Slider[] equalizerBandSliders;
    private readonly TextBlock[] equalizerBandValueTextBlocks;
    private readonly double[] equalizerBandFrequencies = [31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private IReadOnlyList<TrackReference> libraryTracks = Array.Empty<TrackReference>();
    private IReadOnlyList<TrackReference> visibleLibraryTracks = Array.Empty<TrackReference>();
    private IReadOnlyList<TrackReference> filteredLibraryTracks = Array.Empty<TrackReference>();
    private IReadOnlyList<TrackReference> selectedPlaylistTracks = Array.Empty<TrackReference>();
    private IReadOnlyList<FolderReference> folders = Array.Empty<FolderReference>();
    private IReadOnlyList<PlaylistReference> playlists = Array.Empty<PlaylistReference>();
    private IReadOnlyList<PinnedItemReference> pinnedItems = Array.Empty<PinnedItemReference>();
    private IReadOnlyList<TrackReference> homePreviewTracks = Array.Empty<TrackReference>();
    private IReadOnlyList<NamedEqualizerPreset> equalizerPresets = Array.Empty<NamedEqualizerPreset>();
    private readonly Dictionary<string, ImageSource?> artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private FolderReference? selectedFolderContext;
    private PlaylistReference? selectedPlaylistContext;
    private bool isSyncingFolderSelection;
    private bool isSyncingPlaylistSelection;
    private bool isUpdatingEqualizerControls;
    private bool isUpdatingDspControls;
    private bool isLoadingSettingsControls;
    private bool isSyncingLibraryOptions;
    private bool isSyncingPresetSelection;
    private System.Windows.Point? playlistTrackDragStartPoint;
    private Forms.NotifyIcon? trayIcon;
    private HwndSource? windowSource;
    private IntPtr windowHandle;
    private bool mediaHotkeysRegistered;
    private bool isExplicitExitRequested;
    private bool hasShownTrayHint;
    private string? lastNotifiedTrackPath;
    private string? lastLyricsTrackPath;
    private CancellationTokenSource? lyricsLoadCts;
    private string librarySearchText = string.Empty;
    private string librarySortMode = "title";
    private string libraryFilterMode = "all";
    private string? libraryContextFilterType;
    private string? libraryContextFilterValue;
    private int autoScanRefreshGate;
    private readonly string lastFmApiKey;
    private readonly string lastFmSharedSecret;
    private readonly bool hasLastFmBuildCredentials;
    private AppSettingsProfile appSettings = AppSettingsProfile.Default;
    private string activeSection = LibrarySection;

    public MainWindow()
    {
        InitializeComponent();

        equalizerBandSliders =
        [
            EqualizerBand31Slider,
            EqualizerBand62Slider,
            EqualizerBand125Slider,
            EqualizerBand250Slider,
            EqualizerBand500Slider,
            EqualizerBand1000Slider,
            EqualizerBand2000Slider,
            EqualizerBand4000Slider,
            EqualizerBand8000Slider,
            EqualizerBand16000Slider
        ];

        equalizerBandValueTextBlocks =
        [
            EqualizerBand31ValueTextBlock,
            EqualizerBand62ValueTextBlock,
            EqualizerBand125ValueTextBlock,
            EqualizerBand250ValueTextBlock,
            EqualizerBand500ValueTextBlock,
            EqualizerBand1000ValueTextBlock,
            EqualizerBand2000ValueTextBlock,
            EqualizerBand4000ValueTextBlock,
            EqualizerBand8000ValueTextBlock,
            EqualizerBand16000ValueTextBlock
        ];

        WindowsApplicationPaths appPaths;
        (
            playbackOrchestrator,
            libraryImportService,
            trackRepository,
            folderRepository,
            playlistRepository,
            playlistTrackRepository,
            pinnedItemRepository,
            appPaths,
            appSettingsStore,
            equalizerProfileStore,
            dspProfileStore,
            equalizerPresetStore
        ) = BuildServices();

        var lastFmBuildCredentials = LastFmBuildCredentials.Load();
        lastFmApiKey = lastFmBuildCredentials.ApiKey;
        lastFmSharedSecret = lastFmBuildCredentials.SharedSecret;
        hasLastFmBuildCredentials = lastFmBuildCredentials.IsConfigured;

        lyricsService = new LyricsService();
        var lastFmSessionStore = new LastFmSessionStore(appPaths.LastFmSessionFilePath);
        lastFmScrobbleService = new LastFmScrobbleService(lastFmSessionStore);
        libraryAutoScanService = new LibraryAutoScanService();
        libraryAutoScanService.ChangeDetected += LibraryAutoScanService_ChangeDetected;

        smtcService = WindowsSmtcService.Create();
        if (smtcService is not null)
        {
            smtcService.ButtonPressed += SmtcService_ButtonPressed;
        }

        playbackOrchestrator.StatusChanged += PlaybackOrchestrator_StatusChanged;

        positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        positionTimer.Tick += PositionTimer_Tick;
        positionTimer.Start();

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        windowHandle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(windowHandle);
        windowSource?.AddHook(WindowProc);

        // SMTC is the preferred integration path. Register fallback global hotkeys only if SMTC is unavailable.
        if (smtcService is null)
        {
            RegisterMediaHotkeys();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTrayIcon();
        TryRegisterFileAssociations();

        appSettings = await LoadAppSettingsAsync();
        appSettings = NormalizeAppSettings(appSettings);
        await lastFmScrobbleService.InitializeAsync();

        if (!hasLastFmBuildCredentials && appSettings.LastFmScrobblingEnabled)
        {
            appSettings = appSettings with
            {
                LastFmScrobblingEnabled = false
            };
            await TrySaveAppSettingsAsync(appSettings);
        }

        if (lastFmScrobbleService.IsConnected &&
            !string.Equals(appSettings.LastFmUsername, lastFmScrobbleService.Username, StringComparison.Ordinal))
        {
            appSettings = appSettings with
            {
                LastFmUsername = lastFmScrobbleService.Username
            };
            await TrySaveAppSettingsAsync(appSettings);
        }

        activeSection = ResolveInitialSection(appSettings);
        librarySortMode = NormalizeLibrarySortMode(appSettings.LibrarySortMode);
        libraryFilterMode = NormalizeLibraryFilterMode(appSettings.LibraryFilterMode);

        ApplySettingsToUi();

        var restoreResult = appSettings.RestoreLastSession
            ? await playbackOrchestrator.RestoreAsync()
            : new PlaybackRestoreResult(null, null, Array.Empty<TrackReference>());

        BackendNameTextBlock.Text = $"Playback backend: {playbackOrchestrator.BackendDisplayName}";

        libraryTracks = await libraryImportService.GetLibraryTracksAsync();
        visibleLibraryTracks = libraryTracks;
        folders = await folderRepository.GetAllAsync();
        playlists = await LoadPlaylistsWithSmartCountsAsync();
        pinnedItems = await pinnedItemRepository.GetAllAsync();
        equalizerPresets = await LoadEqualizerPresetsAsync();

        RefreshLibraryList();
        RefreshFolderList();
        RefreshPlaylistList();
        RefreshPinnedItemList();
        RefreshEqualizerPresetList();
        ConfigureLibraryAutoScan();
        SyncLibraryViewControls();
        SetActiveSection(activeSection);
        SyncTrackSelection(restoreResult.CurrentTrack);

        if (restoreResult.CurrentTrack is not null)
        {
            CurrentTrackTitleTextBlock.Text = restoreResult.CurrentTrack.Title;
            CurrentTrackMetaTextBlock.Text = BuildTrackMeta(restoreResult.CurrentTrack);
        }

        var savedEqualizerProfile = await equalizerProfileStore.LoadAsync() ?? playbackOrchestrator.Effects.EqualizerProfile;
        ApplyEqualizerProfile(savedEqualizerProfile, persist: false);
        var savedDspProfile = await dspProfileStore.LoadAsync() ?? playbackOrchestrator.Effects.DspProfile;
        ApplyDspProfile(savedDspProfile, persist: false);

        if (appSettings.AutoPlayAfterRestore &&
            restoreResult.CurrentTrack is not null &&
            playbackOrchestrator.State != PlaybackTransportState.Playing)
        {
            playbackOrchestrator.Resume();
        }

        UpdateUi(playbackOrchestrator.State, restoreResult.CurrentTrack);
        RefreshLyricsPanel(restoreResult.CurrentTrack);

        var openedByFileArgument = await TryHandleLaunchFileArgumentsAsync();
        var launchArgs = Environment.GetCommandLineArgs();
        var startedMinimizedFromArg = launchArgs.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));

        if (!openedByFileArgument && (startedMinimizedFromArg || appSettings.StartMinimizedToTray))
        {
            HideToTray(showHint: false);
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!isExplicitExitRequested && appSettings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray(showHint: !hasShownTrayHint);
            hasShownTrayHint = true;
            return;
        }

        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";
        await playbackOrchestrator.SaveSessionAsync(isQueueVisible: true, appVersion: appVersion);
        await equalizerProfileStore.SaveAsync(playbackOrchestrator.Effects.EqualizerProfile);
        await dspProfileStore.SaveAsync(playbackOrchestrator.Effects.DspProfile);
        playbackOrchestrator.Dispose();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        UnregisterMediaHotkeys();
        windowSource?.RemoveHook(WindowProc);
        windowSource = null;
        lyricsLoadCts?.Cancel();
        lyricsLoadCts?.Dispose();
        lyricsLoadCts = null;

        if (smtcService is not null)
        {
            smtcService.ButtonPressed -= SmtcService_ButtonPressed;
            smtcService.Dispose();
        }

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        libraryAutoScanService.ChangeDetected -= LibraryAutoScanService_ChangeDetected;
        libraryAutoScanService.Dispose();
    }

    private (
        PlaybackOrchestrator orchestrator,
        ILibraryImportService libraryImportService,
        ITrackRepository trackRepository,
        IFolderRepository folderRepository,
        IPlaylistRepository playlistRepository,
        IPlaylistTrackRepository playlistTrackRepository,
        IPinnedItemRepository pinnedItemRepository,
        WindowsApplicationPaths appPaths,
        AppSettingsJsonStore appSettingsStore,
        EqualizerProfileJsonStore equalizerProfileStore,
        DspProfileJsonStore dspProfileStore,
        NamedEqualizerPresetJsonStore equalizerPresetStore) BuildServices()
    {
        var profile = new DatabaseProfile(
            ApplicationDirectoryName: "Petrichor",
            DatabaseFileName: "petrichor.db",
            ArtworkCacheDirectoryName: "ArtworkCache");

        var paths = new WindowsApplicationPaths(profile);
        var migrator = new PetrichorSqliteMigrator();
        migrator.EnsureCreatedAsync(paths.DatabaseFilePath).GetAwaiter().GetResult();

        var connectionFactory = new SqliteConnectionFactory(paths.DatabaseFilePath);
        var folderRepo = new SqliteFolderRepository(connectionFactory);
        var trackRepo = new SqliteTrackRepository(connectionFactory);
        var playlistRepo = new SqlitePlaylistRepository(connectionFactory);
        var playlistTrackRepo = new SqlitePlaylistTrackRepository(connectionFactory);
        var pinnedItemRepo = new SqlitePinnedItemRepository(connectionFactory);

        var seedService = new DevelopmentSeedService(
            connectionFactory,
            folderRepo,
            trackRepo,
            playlistRepo,
            playlistTrackRepo);
        seedService.RemoveSeedAsync().GetAwaiter().GetResult();

        ITrackMetadataService trackMetadataService = new TagLibTrackMetadataService();
        ILibraryImportService libraryService = new LibraryImportService(folderRepo, trackRepo, trackMetadataService);

        var queueController = new PlaybackQueueController();
        var stateStore = new PlaybackStateJsonStore(paths.PlaybackStateFilePath);
        var appSettings = new AppSettingsJsonStore(paths.AppSettingsFilePath);
        var playbackSessionService = new PlaybackSessionService(stateStore, trackRepo, playlistTrackRepo, queueController);
        var eqStore = new EqualizerProfileJsonStore(paths.EqualizerProfileFilePath);
        var dspStore = new DspProfileJsonStore(paths.DspProfileFilePath);
        var eqPresetStore = new NamedEqualizerPresetJsonStore(paths.EqualizerPresetsFilePath);
        var playbackBackendFactory = new PlaybackBackendFactory();
        var playbackBackend = playbackBackendFactory.Create(AudioEngineCandidate.NAudio);

        return (
            new PlaybackOrchestrator(playbackBackend, playbackSessionService),
            libraryService,
            trackRepo,
            folderRepo,
            playlistRepo,
            playlistTrackRepo,
            pinnedItemRepo,
            paths,
            appSettings,
            eqStore,
            dspStore,
            eqPresetStore);
    }

    private async void AddLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select a music folder to import into Petrichor",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        var result = await libraryImportService.ImportFolderAsync(dialog.SelectedPath);
        await RefreshCollectionsAsync(refreshPlaylists: true);

        LibraryCenterSummaryTextBlock.Text = $"Imported {result.TracksImported} tracks from {result.Folder.Name}";
        SetActiveSection(LibrarySection);
    }

    private async void OpenAudioFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Audio File",
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg|All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var track = new TrackReference(
            TrackId: null,
            Path: dialog.FileName,
            Title: Path.GetFileNameWithoutExtension(dialog.FileName),
            Artist: null,
            Album: null,
            DurationSeconds: 0);

        await playbackOrchestrator.StartQueueAsync([track], track, QueueSource.Library);
        UpdateUi(playbackOrchestrator.State, track);
    }

    private void TryRegisterFileAssociations()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                FileAssociationRegistrar.EnsureOpenWithRegistration(executablePath);
            }
        }
        catch
        {
            // File associations are best-effort and must not block startup.
        }
    }

    private async Task<bool> TryHandleLaunchFileArgumentsAsync()
    {
        var launchFilePath = ResolveLaunchFilePath(Environment.GetCommandLineArgs());
        if (launchFilePath is null)
        {
            return false;
        }

        var track = new TrackReference(
            TrackId: null,
            Path: launchFilePath,
            Title: Path.GetFileNameWithoutExtension(launchFilePath),
            Artist: null,
            Album: null,
            DurationSeconds: 0);

        await playbackOrchestrator.StartQueueAsync([track], track, QueueSource.Library);
        UpdateUi(playbackOrchestrator.State, track);
        SetActiveSection(HomeSection);
        return true;
    }

    private static string? ResolveLaunchFilePath(IEnumerable<string> args)
    {
        foreach (var argument in args.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.Exists(argument))
            {
                return argument;
            }
        }

        return null;
    }

    private void InitializeTrayIcon()
    {
        if (trayIcon is not null)
        {
            return;
        }

        trayIcon = new Forms.NotifyIcon
        {
            Text = "Petrichor for Windows",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };

        var contextMenu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = new PetrichorTrayMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(15, 21, 27),
            ForeColor = System.Drawing.Color.FromArgb(234, 242, 251),
            Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular)
        };

        var showItem = contextMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        var playPauseItem = contextMenu.Items.Add("Play / Pause", null, (_, _) => TogglePlaybackFromSystem());
        var nextItem = contextMenu.Items.Add("Next", null, async (_, _) => await playbackOrchestrator.PlayNextAsync());
        var previousItem = contextMenu.Items.Add("Previous", null, async (_, _) => await playbackOrchestrator.PlayPreviousAsync(restartThresholdReached: false));
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        var exitItem = contextMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        foreach (var item in new[] { showItem, playPauseItem, nextItem, previousItem, exitItem }.OfType<Forms.ToolStripMenuItem>())
        {
            item.ForeColor = System.Drawing.Color.FromArgb(234, 242, 251);
            item.Padding = new Forms.Padding(12, 6, 12, 6);
        }

        if (exitItem is Forms.ToolStripMenuItem exitMenuItem)
        {
            exitMenuItem.ForeColor = System.Drawing.Color.FromArgb(244, 140, 140);
        }

        trayIcon.ContextMenuStrip = contextMenu;

        trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray(bool showHint)
    {
        Hide();
        ShowInTaskbar = false;

        if (showHint && trayIcon is not null)
        {
            trayIcon.BalloonTipTitle = "Petrichor";
            trayIcon.BalloonTipText = "App is still running in the tray.";
            trayIcon.ShowBalloonTip(2500);
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitFromTray()
    {
        isExplicitExitRequested = true;
        Close();
    }

    private void TogglePlaybackFromSystem()
    {
        Dispatcher.Invoke(() =>
        {
            playbackOrchestrator.TogglePlayPause();
            UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
        });
    }

    private void SmtcService_ButtonPressed(object? sender, SystemMediaTransportControlsButton button)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play:
                    playbackOrchestrator.Resume();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    playbackOrchestrator.Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                case SystemMediaTransportControlsButton.FastForward:
                    await playbackOrchestrator.PlayNextAsync();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                case SystemMediaTransportControlsButton.Rewind:
                    await playbackOrchestrator.PlayPreviousAsync(restartThresholdReached: false);
                    break;
                case SystemMediaTransportControlsButton.Stop:
                    playbackOrchestrator.Stop();
                    break;
            }

            UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
        }, DispatcherPriority.Background);
    }

    private void RegisterMediaHotkeys()
    {
        if (windowHandle == IntPtr.Zero || mediaHotkeysRegistered)
        {
            return;
        }

        RegisterHotKey(windowHandle, HotkeyPlayPauseId, 0, VkMediaPlayPause);
        RegisterHotKey(windowHandle, HotkeyNextId, 0, VkMediaNextTrack);
        RegisterHotKey(windowHandle, HotkeyPreviousId, 0, VkMediaPreviousTrack);
        mediaHotkeysRegistered = true;
    }

    private void UnregisterMediaHotkeys()
    {
        if (windowHandle == IntPtr.Zero || !mediaHotkeysRegistered)
        {
            return;
        }

        UnregisterHotKey(windowHandle, HotkeyPlayPauseId);
        UnregisterHotKey(windowHandle, HotkeyNextId);
        UnregisterHotKey(windowHandle, HotkeyPreviousId);
        mediaHotkeysRegistered = false;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyPlayPauseId:
                TogglePlaybackFromSystem();
                handled = true;
                break;
            case HotkeyNextId:
                _ = Dispatcher.InvokeAsync(async () => await playbackOrchestrator.PlayNextAsync());
                handled = true;
                break;
            case HotkeyPreviousId:
                _ = Dispatcher.InvokeAsync(async () => await playbackOrchestrator.PlayPreviousAsync(restartThresholdReached: false));
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private async void LibraryTracksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || listBox.SelectedItem is not TrackReference track)
        {
            return;
        }

        await playbackOrchestrator.StartQueueAsync(
            filteredLibraryTracks,
            track,
            selectedPlaylistContext is not null ? QueueSource.Playlist : selectedFolderContext is not null ? QueueSource.Folder : QueueSource.Library,
            selectedPlaylistContext?.Id.ToString() ?? selectedFolderContext?.Path);
        UpdateUi(playbackOrchestrator.State, track);
    }

    private void GoToArtistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Artist))
        {
            ApplyLibraryFacetFilter("artist", track.Artist.Trim(), "Artist");
        }
    }

    private void GoToAlbumMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Album))
        {
            ApplyLibraryFacetFilter("album", track.Album.Trim(), "Album");
        }
    }

    private void GoToYearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Year))
        {
            ApplyLibraryFacetFilter("year", track.Year.Trim(), "Year");
        }
    }

    private void GoToGenreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Genre))
        {
            ApplyLibraryFacetFilter("genre", track.Genre.Trim(), "Genre");
        }
    }

    private async void PinArtistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Artist))
        {
            await PinLibraryFacetAsync("artist", track.Artist.Trim(), $"Artist: {track.Artist.Trim()}", "A");
        }
    }

    private async void PinAlbumMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Album))
        {
            await PinLibraryFacetAsync("album", track.Album.Trim(), $"Album: {track.Album.Trim()}", "B");
        }
    }

    private async void PinYearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Year))
        {
            await PinLibraryFacetAsync("year", track.Year.Trim(), $"Year: {track.Year.Trim()}", "Y");
        }
    }

    private async void PinGenreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveTrackFromMenu(sender) is { } track && !string.IsNullOrWhiteSpace(track.Genre))
        {
            await PinLibraryFacetAsync("genre", track.Genre.Trim(), $"Genre: {track.Genre.Trim()}", "G");
        }
    }

    private async void PinPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveCommandParameter<PlaylistReference>(sender, out var playlist))
        {
            playlist = PlaylistsCenterListBox.SelectedItem as PlaylistReference
                ?? PlaylistsListBox.SelectedItem as PlaylistReference;
        }

        if (playlist is null)
        {
            return;
        }

        var alreadyPinned = pinnedItems.Any(item =>
            string.Equals(item.ItemType, "playlist", StringComparison.OrdinalIgnoreCase) &&
            item.PlaylistId == playlist.Id);
        if (alreadyPinned)
        {
            return;
        }

        await pinnedItemRepository.UpsertAsync(new PinnedItemReference(
            Id: null,
            ItemType: "playlist",
            FilterType: null,
            FilterValue: null,
            EntityId: playlist.Id,
            PlaylistId: playlist.Id,
            DisplayName: playlist.Name,
            IconName: "P",
            SortOrder: pinnedItems.Count));
        pinnedItems = await pinnedItemRepository.GetAllAsync();
        RefreshPinnedItemList();
    }

    private async void PinnedItemsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PinnedItemsListBox.SelectedItem is not PinnedItemReference pinnedItem)
        {
            return;
        }

        await NavigateFromPinnedItemAsync(pinnedItem);
    }

    private async void MovePinnedItemUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedItemsListBox.SelectedItem is not PinnedItemReference selectedPinnedItem)
        {
            return;
        }

        var currentIndex = pinnedItems
            .Select((item, index) => new { item, index })
            .Where(entry => entry.item.Id == selectedPinnedItem.Id)
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        if (currentIndex <= 0)
        {
            return;
        }

        await MovePinnedItemAsync(currentIndex, currentIndex - 1, selectedPinnedItem);
    }

    private async void MovePinnedItemDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedItemsListBox.SelectedItem is not PinnedItemReference selectedPinnedItem)
        {
            return;
        }

        var currentIndex = pinnedItems
            .Select((item, index) => new { item, index })
            .Where(entry => entry.item.Id == selectedPinnedItem.Id)
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        if (currentIndex < 0 || currentIndex >= pinnedItems.Count - 1)
        {
            return;
        }

        await MovePinnedItemAsync(currentIndex, currentIndex + 1, selectedPinnedItem);
    }

    private async Task MovePinnedItemAsync(int currentIndex, int targetIndex, PinnedItemReference selectedPinnedItem)
    {
        if (currentIndex < 0 || targetIndex < 0 || currentIndex >= pinnedItems.Count || targetIndex >= pinnedItems.Count)
        {
            return;
        }

        var reorderedItems = pinnedItems.ToList();
        (reorderedItems[currentIndex], reorderedItems[targetIndex]) = (reorderedItems[targetIndex], reorderedItems[currentIndex]);

        var orderedIds = reorderedItems
            .Where(item => item.Id.HasValue)
            .Select(item => item.Id!.Value)
            .ToArray();

        await pinnedItemRepository.ReorderAsync(orderedIds);
        pinnedItems = await pinnedItemRepository.GetAllAsync();
        RefreshPinnedItemList();

        if (selectedPinnedItem.Id is long selectedPinnedItemId)
        {
            PinnedItemsListBox.SelectedItem = pinnedItems.FirstOrDefault(item => item.Id == selectedPinnedItemId);
        }
    }

    private async void RemovePinnedItemMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveCommandParameter<PinnedItemReference>(sender, out var pinnedItem))
        {
            pinnedItem = PinnedItemsListBox.SelectedItem as PinnedItemReference;
        }

        if (pinnedItem?.Id is not long id)
        {
            return;
        }

        await pinnedItemRepository.DeleteAsync(id);
        pinnedItems = await pinnedItemRepository.GetAllAsync();
        RefreshPinnedItemList();
    }

    private async void ClearPinnedItemsButton_Click(object sender, RoutedEventArgs e)
    {
        if (pinnedItems.Count == 0)
        {
            return;
        }

        if (!ShowStyledConfirmationDialog(
                "Clear Quick Access",
                "Remove all pinned sidebar items?",
                confirmLabel: "Clear",
                isDestructive: true))
        {
            return;
        }

        foreach (var item in pinnedItems.Where(item => item.Id.HasValue).ToArray())
        {
            await pinnedItemRepository.DeleteAsync(item.Id!.Value);
        }

        pinnedItems = await pinnedItemRepository.GetAllAsync();
        RefreshPinnedItemList();
    }

    private async void FoldersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || listBox.SelectedItem is not FolderReference folder)
        {
            return;
        }

        var folderTracks = (await libraryImportService.GetLibraryTracksAsync())
            .Where(track => track.Path.StartsWith(folder.Path, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (folderTracks.Length == 0)
        {
            return;
        }

        await playbackOrchestrator.StartQueueAsync(folderTracks, folderTracks[0], QueueSource.Folder, folder.Path);
        UpdateUi(playbackOrchestrator.State, folderTracks[0]);
    }

    private async void PlaylistsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || listBox.SelectedItem is not PlaylistReference playlist)
        {
            return;
        }

        var playlistTracks = await GetTracksForPlaylistAsync(playlist);
        if (playlistTracks.Count == 0)
        {
            return;
        }

        await playbackOrchestrator.StartQueueAsync(playlistTracks, playlistTracks[0], QueueSource.Playlist, playlist.Id.ToString());
        UpdateUi(playbackOrchestrator.State, playlistTracks[0]);
    }

    private async void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = $"Playlist {DateTime.Now:yyyy-MM-dd HH-mm}";
        var playlistName = PromptForText("New Playlist", "Create a new playlist", defaultName);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return;
        }

        var playlist = await CreatePlaylistAsync(playlistName.Trim(), 0);
        await ReloadPlaylistsAsync(playlist.Id);
        SetActiveSection(PlaylistsSection);
    }

    private void PlaylistActionsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistActionsPopup is null)
        {
            return;
        }

        PlaylistActionsPopup.IsOpen = !PlaylistActionsPopup.IsOpen;
    }

    private void SaveCurrentQueueMenuItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistActionsPopup is not null)
        {
            PlaylistActionsPopup.IsOpen = false;
        }

        SaveCurrentQueueButton_Click(sender, e);
    }

    private void AddCurrentTrackMenuItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistActionsPopup is not null)
        {
            PlaylistActionsPopup.IsOpen = false;
        }

        AddCurrentTrackToPlaylistButton_Click(sender, e);
    }

    private void ImportM3uMenuItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistActionsPopup is not null)
        {
            PlaylistActionsPopup.IsOpen = false;
        }

        ImportM3uPlaylistButton_Click(sender, e);
    }

    private void ExportM3uMenuItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistActionsPopup is not null)
        {
            PlaylistActionsPopup.IsOpen = false;
        }

        ExportSelectedPlaylistButton_Click(sender, e);
    }

    private async void SaveCurrentQueueButton_Click(object sender, RoutedEventArgs e)
    {
        var queueTracks = playbackOrchestrator.QueueController.Queue
            .Where(track => track.TrackId.HasValue)
            .ToArray();

        if (queueTracks.Length == 0)
        {
            ShowStyledInfoDialog(
                "Save Current Queue",
                "The current queue does not contain imported library tracks yet, so there is nothing to save as a playlist.");
            return;
        }

        var defaultName = BuildPlaylistName();
        var playlistName = PromptForText("New Playlist", "Save current queue as playlist", defaultName);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return;
        }

        var playlist = await CreatePlaylistAsync(playlistName.Trim(), queueTracks.Length);
        await playlistTrackRepository.ReplaceTracksAsync(playlist.Id, queueTracks.Select(track => track.TrackId!.Value));
        await ReloadPlaylistsAsync(playlist.Id);
        SetActiveSection(PlaylistsSection);

        ShowStyledInfoDialog(
            "Playlist Saved",
            $"Saved {queueTracks.Length} tracks to playlist '{playlist.Name}'.");
    }

    private async void AddCurrentTrackToPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var currentTrack = playbackOrchestrator.CurrentTrack;
        if (currentTrack?.TrackId is not long trackId)
        {
            ShowStyledInfoDialog(
                "Add Current Track",
                "Only imported library tracks can be added to playlists right now.");
            return;
        }

        var selectedPlaylist = GetSelectedPlaylist();
        if (selectedPlaylist is null)
        {
            var defaultName = $"Playlist {DateTime.Now:yyyy-MM-dd HH-mm}";
            var playlistName = PromptForText("New Playlist", "No playlist selected. Create a new playlist for the current track", defaultName);
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return;
            }

            selectedPlaylist = await CreatePlaylistAsync(playlistName.Trim(), 1);
        }

        if (!selectedPlaylist.IsContentEditable)
        {
            ShowStyledInfoDialog(
                "Add Current Track",
                "This playlist is smart/read-only. Choose a manual playlist or create a new one.");
            return;
        }

        var playlistTracks = await playlistTrackRepository.GetTracksForPlaylistAsync(selectedPlaylist.Id);
        if (playlistTracks.Any(track => track.TrackId == trackId))
        {
            ShowStyledInfoDialog(
                "Add Current Track",
                "The current track is already in this playlist.");
            return;
        }

        var updatedTrackIds = playlistTracks
            .Where(track => track.TrackId.HasValue)
            .Select(track => track.TrackId!.Value)
            .Append(trackId)
            .ToArray();

        await playlistTrackRepository.ReplaceTracksAsync(selectedPlaylist.Id, updatedTrackIds);
        await ReloadPlaylistsAsync(selectedPlaylist.Id);
    }

    private async void RenamePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlaylist = ResolvePlaylistFromSender(sender);
        if (selectedPlaylist is null)
        {
            ShowStyledInfoDialog(
                "Rename Playlist",
                "Select a playlist first.");
            return;
        }

        if (!selectedPlaylist.IsUserEditable)
        {
            ShowStyledInfoDialog(
                "Rename Playlist",
                "This playlist is system-managed and cannot be renamed.");
            return;
        }

        var updatedName = PromptForText("Rename Playlist", "Enter a new playlist name", selectedPlaylist.Name);
        if (string.IsNullOrWhiteSpace(updatedName))
        {
            return;
        }

        await playlistRepository.UpsertAsync(selectedPlaylist with { Name = updatedName.Trim() });
        await ReloadPlaylistsAsync(selectedPlaylist.Id);
    }

    private async void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlaylist = ResolvePlaylistFromSender(sender);
        if (selectedPlaylist is null)
        {
            ShowStyledInfoDialog(
                "Delete Playlist",
                "Select a playlist first.");
            return;
        }

        if (!selectedPlaylist.IsUserEditable)
        {
            ShowStyledInfoDialog(
                "Delete Playlist",
                "This playlist is system-managed and cannot be deleted.");
            return;
        }

        if (!ShowStyledConfirmationDialog(
                "Delete Playlist",
                $"Delete playlist '{selectedPlaylist.Name}'?",
                confirmLabel: "Delete",
                isDestructive: true))
        {
            return;
        }

        await playlistRepository.DeleteAsync(selectedPlaylist.Id);
        await ReloadPlaylistsAsync();
    }

    private async void SaveLibraryViewAsSmartPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (filteredLibraryTracks.Count == 0)
        {
            ShowStyledInfoDialog(
                "Smart Playlist",
                "Current library view is empty. Adjust filters or search, then try again.");
            return;
        }

        var defaultName = $"Smart View {DateTime.Now:yyyy-MM-dd HH-mm}";
        var playlistName = PromptForText("New Smart Playlist", "Save current library view as smart playlist", defaultName);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return;
        }

        var smartCriteriaJson = BuildSmartPlaylistCriteriaFromCurrentLibraryView();
        var playlist = await CreatePlaylistAsync(
            playlistName.Trim(),
            filteredLibraryTracks.Count,
            playlistType: "smart",
            isUserEditable: true,
            isContentEditable: false,
            smartCriteriaJson: smartCriteriaJson);

        await ReloadPlaylistsAsync(playlist.Id);
        SetActiveSection(PlaylistsSection);

        ShowStyledInfoDialog(
            "Smart Playlist",
            $"Created smart playlist '{playlist.Name}' from current library view.");
    }

    private async void ImportM3uPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import M3U Playlist",
            Filter = "Playlist Files|*.m3u;*.m3u8|All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var parsedPaths = ParseM3uEntries(dialog.FileName);
        if (parsedPaths.Count == 0)
        {
            ShowStyledInfoDialog(
                "Import M3U",
                "No track entries were found in the selected playlist file.");
            return;
        }

        var matchedTrackIds = new List<long>();
        var missingEntries = 0;
        foreach (var path in parsedPaths)
        {
            var resolvedPath = ResolveM3uPath(dialog.FileName, path);
            if (resolvedPath is null)
            {
                missingEntries++;
                continue;
            }

            var track = await trackRepository.GetByPathAsync(resolvedPath);
            if (track?.TrackId is long trackId)
            {
                matchedTrackIds.Add(trackId);
            }
            else
            {
                missingEntries++;
            }
        }

        if (matchedTrackIds.Count == 0)
        {
            ShowStyledInfoDialog(
                "Import M3U",
                "No playlist entries matched tracks currently in your library.");
            return;
        }

        var playlistName = Path.GetFileNameWithoutExtension(dialog.FileName);
        var playlist = await CreatePlaylistAsync(
            string.IsNullOrWhiteSpace(playlistName) ? $"Imported Playlist {DateTime.Now:yyyy-MM-dd HH-mm}" : playlistName,
            matchedTrackIds.Count);
        await playlistTrackRepository.ReplaceTracksAsync(playlist.Id, matchedTrackIds.Distinct().ToArray());
        await ReloadPlaylistsAsync(playlist.Id);
        SetActiveSection(PlaylistsSection);

        ShowStyledInfoDialog(
            "Import M3U",
            $"Imported {matchedTrackIds.Count} tracks. Missing entries: {missingEntries}.");
    }

    private async void ExportSelectedPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlaylist = GetSelectedPlaylist();
        if (selectedPlaylist is null)
        {
            ShowStyledInfoDialog(
                "Export M3U",
                "Select a playlist first.");
            return;
        }

        var tracks = await GetTracksForPlaylistAsync(selectedPlaylist);
        if (tracks.Count == 0)
        {
            ShowStyledInfoDialog(
                "Export M3U",
                "Selected playlist has no tracks to export.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export M3U Playlist",
            Filter = "M3U Playlist|*.m3u8|M3U Playlist (ANSI)|*.m3u|All Files|*.*",
            FileName = SanitizeFileName(selectedPlaylist.Name) + ".m3u8",
            AddExtension = true,
            DefaultExt = ".m3u8"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var outputLines = new List<string>(tracks.Count + 1)
        {
            "#EXTM3U"
        };

        foreach (var track in tracks)
        {
            var durationSeconds = Math.Max(0, (int)Math.Round(track.DurationSeconds));
            var artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
            outputLines.Add($"#EXTINF:{durationSeconds},{artist} - {track.Title}");
            outputLines.Add(track.Path);
        }

        await File.WriteAllLinesAsync(dialog.FileName, outputLines, CancellationToken.None);

        ShowStyledInfoDialog(
            "Export M3U",
            $"Exported {tracks.Count} tracks to '{Path.GetFileName(dialog.FileName)}'.");
    }

    private async void RescanAllFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        await RescanAllFoldersAsync(showSummary: true);
    }

    private async void RemoveSelectedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFolder = FoldersCenterListBox.SelectedItem as FolderReference
            ?? FoldersListBox.SelectedItem as FolderReference
            ?? selectedFolderContext;
        if (selectedFolder is null)
        {
            ShowStyledInfoDialog(
                "Remove Folder",
                "Select a folder first.");
            return;
        }

        if (!ShowStyledConfirmationDialog(
                "Remove Folder",
                $"Remove folder '{selectedFolder.Name}' from library?\nTracks from this folder will be removed from the database.",
                confirmLabel: "Remove",
                isDestructive: true))
        {
            return;
        }

        await folderRepository.DeleteByPathAsync(selectedFolder.Path);
        selectedFolderContext = null;
        await RefreshCollectionsAsync(refreshPlaylists: true);
        SetActiveSection(FoldersSection);
    }

    private async void RemovePlaylistTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPlaylistContext is null)
        {
            ShowStyledInfoDialog(
                "Playlist Tracks",
                "Select a playlist first.");
            return;
        }

        if (!selectedPlaylistContext.IsContentEditable)
        {
            ShowStyledInfoDialog(
                "Playlist Tracks",
                "This smart playlist is read-only and cannot be edited directly.");
            return;
        }

        if (PlaylistTracksListBox.SelectedItem is not TrackReference selectedTrack)
        {
            ShowStyledInfoDialog(
                "Playlist Tracks",
                "Select a track inside the playlist first.");
            return;
        }

        var updatedTracks = selectedPlaylistTracks
            .Where(track => !TracksMatch(track, selectedTrack))
            .ToArray();

        await SavePlaylistTrackOrderAsync(selectedPlaylistContext, updatedTracks, reloadPlaylists: true);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        playbackOrchestrator.TogglePlayPause();
        UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        var restartThresholdReached = playbackOrchestrator.Position > TimeSpan.FromSeconds(3);
        await playbackOrchestrator.PlayPreviousAsync(restartThresholdReached);
        UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await playbackOrchestrator.PlayNextAsync();
        UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        playbackOrchestrator.ToggleShuffle();
        UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        playbackOrchestrator.ToggleRepeatMode();
        UpdateUi(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        playbackOrchestrator.SetVolume(e.NewValue);
    }

    private void PlaylistTracksListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        playlistTrackDragStartPoint = e.GetPosition(PlaylistTracksListBox);
    }

    private void PlaylistTracksListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            playlistTrackDragStartPoint is null ||
            selectedPlaylistContext?.IsContentEditable != true)
        {
            return;
        }

        var currentPosition = e.GetPosition(PlaylistTracksListBox);
        if (Math.Abs(currentPosition.X - playlistTrackDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - playlistTrackDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (PlaylistTracksListBox.SelectedItem is TrackReference selectedTrack)
        {
            DragDrop.DoDragDrop(PlaylistTracksListBox, selectedTrack, System.Windows.DragDropEffects.Move);
        }
    }

    private void PlaylistListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(dependencyObject);
        if (scrollViewer is null)
        {
            return;
        }

        var nextOffset = scrollViewer.VerticalOffset - (e.Delta / 4.0);
        var clampedOffset = Math.Max(0, Math.Min(nextOffset, scrollViewer.ScrollableHeight));
        scrollViewer.ScrollToVerticalOffset(clampedOffset);
        e.Handled = true;
    }

    private void PlaylistTracksListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = selectedPlaylistContext?.IsContentEditable == true && e.Data.GetDataPresent(typeof(TrackReference))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void PlaylistTracksListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (selectedPlaylistContext is null || !e.Data.GetDataPresent(typeof(TrackReference)))
        {
            return;
        }

        if (!selectedPlaylistContext.IsContentEditable)
        {
            return;
        }

        if (e.Data.GetData(typeof(TrackReference)) is not TrackReference draggedTrack)
        {
            return;
        }

        var targetTrack = ResolvePlaylistTrackDropTarget(e.OriginalSource as DependencyObject);
        if (targetTrack is null || TracksMatch(draggedTrack, targetTrack))
        {
            return;
        }

        var reorderedTracks = selectedPlaylistTracks.ToList();
        var oldIndex = reorderedTracks.FindIndex(track => TracksMatch(track, draggedTrack));
        var newIndex = reorderedTracks.FindIndex(track => TracksMatch(track, targetTrack));

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        var movedTrack = reorderedTracks[oldIndex];
        reorderedTracks.RemoveAt(oldIndex);
        reorderedTracks.Insert(newIndex, movedTrack);

        await SavePlaylistTrackOrderAsync(selectedPlaylistContext, reorderedTracks, reloadPlaylists: false);
        PlaylistTracksListBox.SelectedItem = movedTrack;
        PlaylistTracksListBox.ScrollIntoView(movedTrack);
    }

    private async void PlaylistTracksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var clickedTrack = ResolvePlaylistTrackDropTarget(e.OriginalSource as DependencyObject)
                ?? PlaylistTracksListBox.SelectedItem as TrackReference;
            if (clickedTrack is null)
            {
                return;
            }

            await StartPlaylistTrackPlaybackAsync(clickedTrack);
        }
        catch
        {
            ShowStyledInfoDialog(
                "Playback Error",
                "Could not start the selected playlist track.");
        }
    }

    private async Task StartPlaylistTrackPlaybackAsync(TrackReference track)
    {
        if (selectedPlaylistContext is null)
        {
            return;
        }

        var queueTracks = PlaylistTracksListBox.Items.OfType<TrackReference>().ToArray();
        if (queueTracks.Length == 0)
        {
            return;
        }

        await playbackOrchestrator.StartQueueAsync(
            queueTracks,
            track,
            QueueSource.Playlist,
            selectedPlaylistContext.Id.ToString());

        UpdateUi(playbackOrchestrator.State, track);
    }

    private void LibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || LibraryTracksListBox is null)
        {
            return;
        }

        librarySearchText = LibrarySearchTextBox.Text;
        ApplyLibraryViewState(focusCurrentTrack: false);
    }

    private void LibrarySortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LibraryTracksListBox is null || isSyncingLibraryOptions)
        {
            return;
        }

        if (LibrarySortComboBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            librarySortMode = tag;
            ApplyLibraryViewState(focusCurrentTrack: false);
            SyncLibraryViewControls();

            appSettings = appSettings with
            {
                LibrarySortMode = tag
            };
            _ = TrySaveAppSettingsAsync(appSettings);
        }
    }

    private void LibraryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LibraryTracksListBox is null || isSyncingLibraryOptions)
        {
            return;
        }

        if (LibraryFilterComboBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            libraryFilterMode = tag;
            ApplyLibraryViewState(focusCurrentTrack: false);
            SyncLibraryViewControls();

            appSettings = appSettings with
            {
                LibraryFilterMode = tag
            };
            _ = TrySaveAppSettingsAsync(appSettings);
        }
    }

    private async void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || isLoadingSettingsControls)
        {
            return;
        }

        if (RestoreSessionCheckBox.IsChecked != true && AutoPlayAfterRestoreCheckBox.IsChecked == true)
        {
            isLoadingSettingsControls = true;
            AutoPlayAfterRestoreCheckBox.IsChecked = false;
            isLoadingSettingsControls = false;
        }

        RefreshSettingsControlAvailability();

        appSettings = appSettings with
        {
            StartMinimizedToTray = StartMinimizedCheckBox.IsChecked == true,
            RestoreLastSession = RestoreSessionCheckBox.IsChecked == true,
            AutoPlayAfterRestore = AutoPlayAfterRestoreCheckBox.IsChecked == true,
            MinimizeToTrayOnClose = MinimizeToTrayOnCloseCheckBox.IsChecked == true,
            ShowTrackNotifications = TrackNotificationsCheckBox.IsChecked == true,
            AutoScanLibraryEnabled = AutoScanLibraryCheckBox.IsChecked == true,
            RememberLastSection = RememberLastSectionCheckBox.IsChecked == true,
            OnlineLyricsEnabled = SettingsOnlineLyricsEnabledCheckBox.IsChecked == true,
            LastFmScrobblingEnabled = hasLastFmBuildCredentials && SettingsLastFmScrobblingEnabledCheckBox.IsChecked == true,
            LastActiveSection = RememberLastSectionCheckBox.IsChecked == true
                ? activeSection
                : appSettings.LastActiveSection
        };

        await TrySaveAppSettingsAsync(appSettings);
        ConfigureLibraryAutoScan();
        RefreshLastFmUi();

        if (sender == SettingsOnlineLyricsEnabledCheckBox)
        {
            lastLyricsTrackPath = null;
            RefreshLyricsPanel(playbackOrchestrator.CurrentTrack);
        }
    }

    private async void SettingsStartupSectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded ||
            isLoadingSettingsControls ||
            RememberLastSectionCheckBox.IsChecked == true ||
            SettingsStartupSectionComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        appSettings = appSettings with
        {
            StartupSection = NormalizeSection(tag)
        };

        await TrySaveAppSettingsAsync(appSettings);
    }

    private async void ConnectLastFmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!hasLastFmBuildCredentials)
        {
            ShowStyledInfoDialog(
                "Last.fm",
                "This build does not include Last.fm API credentials. Add them in Windows/Configuration/Secrets.props and rebuild.");
            RefreshLastFmUi();
            return;
        }

        var connectResult = await lastFmScrobbleService.ConnectAsync(
            lastFmApiKey,
            lastFmSharedSecret);

        if (!connectResult.Success)
        {
            ShowStyledInfoDialog("Last.fm", connectResult.Message);
            RefreshLastFmUi();
            return;
        }

        appSettings = appSettings with
        {
            LastFmUsername = lastFmScrobbleService.Username
        };
        await TrySaveAppSettingsAsync(appSettings);

        RefreshLastFmUi();
        ShowStyledInfoDialog("Last.fm", connectResult.Message);
    }

    private async void DisconnectLastFmButton_Click(object sender, RoutedEventArgs e)
    {
        lastFmScrobbleService.Disconnect();
        appSettings = appSettings with
        {
            LastFmUsername = string.Empty
        };
        await TrySaveAppSettingsAsync(appSettings);
        RefreshLastFmUi();
    }

    private void SettingsEffectsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || isLoadingSettingsControls)
        {
            return;
        }

        if (sender == SettingsEqEnabledCheckBox && playbackOrchestrator.Effects.IsAvailable)
        {
            var updated = playbackOrchestrator.Effects.EqualizerProfile with
            {
                IsEnabled = SettingsEqEnabledCheckBox.IsChecked == true
            };
            ApplyEqualizerProfile(updated);
            return;
        }

        if (sender == SettingsLoudnessEnabledCheckBox && playbackOrchestrator.BackendCapabilities.SupportsDspEffects)
        {
            var updated = playbackOrchestrator.Effects.DspProfile with
            {
                LoudnessEnabled = SettingsLoudnessEnabledCheckBox.IsChecked == true
            };
            ApplyDspProfile(updated);
            return;
        }

        if (sender == SettingsReplayGainEnabledCheckBox && playbackOrchestrator.BackendCapabilities.SupportsReplayGain)
        {
            var updated = playbackOrchestrator.Effects.DspProfile with
            {
                ReplayGainEnabled = SettingsReplayGainEnabledCheckBox.IsChecked == true
            };
            ApplyDspProfile(updated);
        }
    }

    private void EqualizerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!playbackOrchestrator.Effects.IsAvailable)
        {
            ShowStyledInfoDialog(
                "Equalizer",
                "The current playback backend does not expose a live equalizer yet.");
            return;
        }

        var currentProfile = playbackOrchestrator.Effects.EqualizerProfile;
        var updatedProfile = currentProfile with
        {
            IsEnabled = !currentProfile.IsEnabled
        };

        ApplyEqualizerProfile(updatedProfile);
    }

    private void EqualizerResetButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyEqualizerProfile(EqualizerProfile.Flat);
    }

    private void LoudnessToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!playbackOrchestrator.Effects.IsAvailable)
        {
            return;
        }

        var updated = playbackOrchestrator.Effects.DspProfile with
        {
            LoudnessEnabled = !playbackOrchestrator.Effects.DspProfile.LoudnessEnabled
        };

        ApplyDspProfile(updated);
    }

    private void ReplayGainToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!playbackOrchestrator.BackendCapabilities.SupportsReplayGain)
        {
            ShowStyledInfoDialog(
                "ReplayGain",
                "The active backend does not expose replay gain support.");
            return;
        }

        var updated = playbackOrchestrator.Effects.DspProfile with
        {
            ReplayGainEnabled = !playbackOrchestrator.Effects.DspProfile.ReplayGainEnabled
        };

        ApplyDspProfile(updated);
    }

    private void DspControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isUpdatingDspControls)
        {
            return;
        }

        var current = playbackOrchestrator.Effects.DspProfile;
        var updated = current with
        {
            LoudnessGainDb = LoudnessGainSlider.Value
        };

        ApplyDspProfile(updated, refreshUi: false);
        RefreshDspUi(updated);
    }

    private async void SaveEqualizerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = BuildSuggestedPresetName();
        var presetName = PromptForText("Save EQ Preset", "Save the current EQ profile as a reusable preset", defaultName);
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        var trimmedName = presetName.Trim();
        var profile = playbackOrchestrator.Effects.EqualizerProfile;
        var customPresets = equalizerPresets.Where(preset => !preset.IsBuiltIn).ToList();
        var existingPresetIndex = customPresets.FindIndex(preset => string.Equals(preset.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

        if (existingPresetIndex >= 0)
        {
            customPresets[existingPresetIndex] = new NamedEqualizerPreset(trimmedName, profile, false);
        }
        else
        {
            customPresets.Add(new NamedEqualizerPreset(trimmedName, profile, false));
        }

        await equalizerPresetStore.SaveAsync(customPresets);
        equalizerPresets = await LoadEqualizerPresetsAsync();
        RefreshEqualizerPresetList(trimmedName);
    }

    private async void ImportEqualizerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Petrichor EQ Preset (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Import EQ Preset"
        };

        if (openDialog.ShowDialog(this) != true)
        {
            return;
        }

        ImportedEqualizerPreset? importedPreset;
        try
        {
            await using var stream = File.OpenRead(openDialog.FileName);
            importedPreset = await JsonSerializer.DeserializeAsync<ImportedEqualizerPreset>(stream);
        }
        catch
        {
            ShowStyledInfoDialog(
                "Import EQ Preset",
                "Could not read preset file. Please choose a valid Petrichor EQ preset JSON file.");
            return;
        }

        if (importedPreset is null || string.IsNullOrWhiteSpace(importedPreset.Name))
        {
            ShowStyledInfoDialog(
                "Import EQ Preset",
                "Preset file is missing required data.");
            return;
        }

        var importedName = importedPreset.Name.Trim();
        var profile = importedPreset.Profile;
        var customPresets = equalizerPresets.Where(preset => !preset.IsBuiltIn).ToList();

        var resolvedName = importedName;
        var suffix = 2;
        while (customPresets.Any(preset => string.Equals(preset.Name, resolvedName, StringComparison.OrdinalIgnoreCase)))
        {
            resolvedName = $"{importedName} ({suffix})";
            suffix++;
        }

        customPresets.Add(new NamedEqualizerPreset(resolvedName, profile, false));
        await equalizerPresetStore.SaveAsync(customPresets);

        equalizerPresets = await LoadEqualizerPresetsAsync();
        RefreshEqualizerPresetList(resolvedName);
        ApplyEqualizerProfile(profile);
    }

    private async void ExportEqualizerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (EqualizerPresetComboBox.SelectedItem is not NamedEqualizerPreset selectedPreset)
        {
            ShowStyledInfoDialog(
                "Export EQ Preset",
                "Select a preset first.");
            return;
        }

        var safeName = SanitizeFileName(selectedPreset.Name);
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Petrichor EQ Preset (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"{safeName}.json",
            Title = "Export EQ Preset",
            OverwritePrompt = true
        };

        if (saveDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var exportPreset = new ImportedEqualizerPreset(selectedPreset.Name, selectedPreset.Profile);
            await using var stream = File.Create(saveDialog.FileName);
            await JsonSerializer.SerializeAsync(
                stream,
                exportPreset,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                });
        }
        catch
        {
            ShowStyledInfoDialog(
                "Export EQ Preset",
                "Could not export preset to the selected path.");
            return;
        }

        ShowStyledInfoDialog(
            "Export EQ Preset",
            $"Preset '{selectedPreset.Name}' exported successfully.");
    }

    private async void DeleteEqualizerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (EqualizerPresetComboBox.SelectedItem is not NamedEqualizerPreset selectedPreset || selectedPreset.IsBuiltIn)
        {
            ShowStyledInfoDialog(
                "Delete EQ Preset",
                "Select a custom preset to delete. Built-in presets stay available.");
            return;
        }

        if (!ShowStyledConfirmationDialog(
                "Delete EQ Preset",
                $"Delete preset '{selectedPreset.Name}'?",
                confirmLabel: "Delete",
                isDestructive: true))
        {
            return;
        }

        var customPresets = equalizerPresets
            .Where(preset => !preset.IsBuiltIn && !string.Equals(preset.Name, selectedPreset.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await equalizerPresetStore.SaveAsync(customPresets);
        equalizerPresets = await LoadEqualizerPresetsAsync();
        RefreshEqualizerPresetList(EqualizerPresets.FlatName);
    }

    private void EqualizerPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSyncingPresetSelection || EqualizerPresetComboBox.SelectedItem is not NamedEqualizerPreset selectedPreset)
        {
            return;
        }

        ApplyEqualizerProfile(selectedPreset.Profile);
    }

    private void EqualizerControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isUpdatingEqualizerControls)
        {
            return;
        }

        UpdateEqualizerValueLabels();

        if (!playbackOrchestrator.Effects.IsAvailable)
        {
            return;
        }

        var currentProfile = playbackOrchestrator.Effects.EqualizerProfile;
        var updatedProfile = new EqualizerProfile(
            IsEnabled: currentProfile.IsEnabled,
            PreampDb: EqualizerPreampSlider.Value,
            Bands: equalizerBandFrequencies
                .Select((frequency, index) => new EqualizerBand(
                    FrequencyHz: frequency,
                    GainDb: equalizerBandSliders[index].Value,
                    Q: currentProfile.Bands.ElementAtOrDefault(index)?.Q ?? 1.0))
                .ToArray());

        ApplyEqualizerProfile(updatedProfile, refreshUi: false);
        RefreshEqualizerButtonState(updatedProfile);
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (playbackOrchestrator.Duration is not { } duration || duration.TotalSeconds <= 0)
        {
            return;
        }

        var targetSeconds = PositionSlider.Value * duration.TotalSeconds;
        playbackOrchestrator.Seek(TimeSpan.FromSeconds(targetSeconds));
        UpdatePositionUi();
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        UpdatePositionUi();
        _ = ProcessLastFmSnapshotAsync(playbackOrchestrator.State, playbackOrchestrator.CurrentTrack);
    }

    private void PlaybackOrchestrator_StatusChanged(object? sender, PlaybackStatusSnapshot e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUi(e.State, e.CurrentTrack);
            Dispatcher.BeginInvoke(() => SyncTrackSelection(e.CurrentTrack), DispatcherPriority.Background);
        });
    }

    private void SidebarItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string section })
        {
            SetActiveSection(section);
        }
    }

    private async void PlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSyncingPlaylistSelection || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        try
        {
            isSyncingPlaylistSelection = true;
            var selectedPlaylist = listBox.SelectedItem as PlaylistReference;
            PlaylistsListBox.SelectedItem = selectedPlaylist;
            PlaylistsCenterListBox.SelectedItem = selectedPlaylist;
        }
        finally
        {
            isSyncingPlaylistSelection = false;
        }

        await ApplyPlaylistContextAsync(listBox.SelectedItem as PlaylistReference);
    }

    private async void FoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSyncingFolderSelection || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        try
        {
            isSyncingFolderSelection = true;
            var selectedFolder = listBox.SelectedItem as FolderReference;
            FoldersListBox.SelectedItem = selectedFolder;
            FoldersCenterListBox.SelectedItem = selectedFolder;
        }
        finally
        {
            isSyncingFolderSelection = false;
        }

        await ApplyFolderContextAsync(listBox.SelectedItem as FolderReference);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateUi(PlaybackTransportState state, TrackReference? track)
    {
        var previousNotifiedPath = lastNotifiedTrackPath;
        TransportStateTextBlock.Text = state.ToString();
        CurrentTrackTitleTextBlock.Text = track?.Title ?? "No track loaded";
        CurrentTrackMetaTextBlock.Text = track is null ? "Open a local file to start playback" : BuildTrackMeta(track);
        CurrentTrackDurationNowPlayingTextBlock.Text = $"Duration {FormatTime(playbackOrchestrator.Duration ?? TimeSpan.Zero)}";
        QueueStateTextBlock.Text = $"{playbackOrchestrator.QueueController.Queue.Count} tracks";
        RepeatShuffleTextBlock.Text = $"{playbackOrchestrator.QueueController.RepeatMode} / {(playbackOrchestrator.QueueController.ShuffleEnabled ? "On" : "Off")}";

        PlayPauseButton.Content = state == PlaybackTransportState.Playing ? "Pause" : "Play";
        ShuffleButton.Content = playbackOrchestrator.QueueController.ShuffleEnabled ? "Shuffle On" : "Shuffle Off";
        RepeatButton.Content = $"Repeat {playbackOrchestrator.QueueController.RepeatMode}";
        PlayPauseButton.Tag = state == PlaybackTransportState.Playing ? "Active" : null;
        ShuffleButton.Tag = playbackOrchestrator.QueueController.ShuffleEnabled ? "Active" : null;
        RepeatButton.Tag = playbackOrchestrator.QueueController.RepeatMode != RepeatMode.Off ? "Active" : null;
        BackendNameTextBlock.Text = $"Playback backend: {playbackOrchestrator.BackendDisplayName}";
        MaximizeGlyphTextBlock.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        VolumeSlider.Value = playbackOrchestrator.Volume;
        UpdateEqualizerAvailability();
        UpdatePositionUi();
        UpdateNowPlayingArtwork(track);
        RefreshLyricsPanel(track);
        UpdateSmtcSession(state, track);
        _ = ProcessLastFmSnapshotAsync(state, track);

        if (track is not null &&
            state == PlaybackTransportState.Playing &&
            !string.Equals(previousNotifiedPath, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            ShowTrackNotification(track);
            lastNotifiedTrackPath = track.Path;
        }
        else if (track is null)
        {
            lastNotifiedTrackPath = null;
        }
    }

    private void RefreshLibraryList()
    {
        ApplyLibraryViewState();
        LibraryCenterSummaryTextBlock.Text = BuildVisibleLibrarySummary();

        RefreshHomeLibraryPreview(playbackOrchestrator.CurrentTrack);
    }

    private void RefreshFolderList()
    {
        FoldersListBox.ItemsSource = null;
        FoldersListBox.ItemsSource = folders;
        FoldersCenterListBox.ItemsSource = null;
        FoldersCenterListBox.ItemsSource = folders;
        FoldersCenterSummaryTextBlock.Text = folders.Count == 0
            ? "No folders added"
            : $"{folders.Count} folders";
        FoldersSummaryTextBlock.Text = folders.Count == 0
            ? "No folders added"
            : $"{folders.Count} folders";
    }

    private void RefreshPlaylistList()
    {
        PlaylistsListBox.ItemsSource = null;
        PlaylistsListBox.ItemsSource = playlists;
        PlaylistsCenterListBox.ItemsSource = null;
        PlaylistsCenterListBox.ItemsSource = playlists;
        PlaylistsCenterSummaryTextBlock.Text = playlists.Count == 0
            ? "No playlists available"
            : $"{playlists.Count} playlists";
        PlaylistsSummaryTextBlock.Text = playlists.Count == 0
            ? "No playlists available"
            : $"{playlists.Count} playlists";
    }

    private void RefreshPlaylistTrackList()
    {
        PlaylistTracksListBox.ItemsSource = null;
        PlaylistTracksListBox.ItemsSource = selectedPlaylistTracks;
        PlaylistTracksSummaryTextBlock.Text = selectedPlaylistContext is null
            ? "Select a playlist to inspect its tracks"
            : selectedPlaylistTracks.Count == 0
                ? $"Playlist '{selectedPlaylistContext.Name}' is empty"
                : $"{selectedPlaylistTracks.Count} tracks in '{selectedPlaylistContext.Name}'";
        RemovePlaylistTrackButton.IsEnabled =
            selectedPlaylistContext is not null &&
            selectedPlaylistContext.IsContentEditable &&
            selectedPlaylistTracks.Count > 0;
    }

    private void RefreshPinnedItemList()
    {
        if (PinnedItemsListBox is null)
        {
            return;
        }

        PinnedItemsListBox.ItemsSource = null;
        PinnedItemsListBox.ItemsSource = pinnedItems;
    }

    private void ConfigureLibraryAutoScan()
    {
        libraryAutoScanService.SetEnabled(appSettings.AutoScanLibraryEnabled);
        libraryAutoScanService.UpdateFolders(folders.Select(folder => folder.Path));
    }

    private void LibraryAutoScanService_ChangeDetected(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(
            async () =>
            {
                if (!appSettings.AutoScanLibraryEnabled || !IsLoaded)
                {
                    return;
                }

                if (Interlocked.Exchange(ref autoScanRefreshGate, 1) == 1)
                {
                    return;
                }

                try
                {
                    var currentFolders = await folderRepository.GetAllAsync();
                    foreach (var folder in currentFolders)
                    {
                        if (!Directory.Exists(folder.Path))
                        {
                            continue;
                        }

                        await libraryImportService.ImportFolderAsync(folder.Path);
                    }

                    await RefreshCollectionsAsync(refreshPlaylists: true);
                    LibraryCenterSummaryTextBlock.Text = $"Library auto-scanned at {DateTime.Now:HH:mm:ss}";
                }
                catch
                {
                    // Auto-scan refresh is best effort and should never crash the app loop.
                }
                finally
                {
                    Interlocked.Exchange(ref autoScanRefreshGate, 0);
                }
            },
            DispatcherPriority.Background);
    }

    private async Task RefreshCollectionsAsync(bool refreshPlaylists)
    {
        libraryTracks = await libraryImportService.GetLibraryTracksAsync();
        folders = await folderRepository.GetAllAsync();

        if (refreshPlaylists)
        {
            playlists = await LoadPlaylistsWithSmartCountsAsync();
        }

        var playlistId = selectedPlaylistContext?.Id;
        var folderPath = selectedFolderContext?.Path;

        if (playlistId is not null)
        {
            selectedPlaylistContext = playlists.FirstOrDefault(playlist => playlist.Id == playlistId.Value);
            selectedPlaylistTracks = selectedPlaylistContext is null
                ? Array.Empty<TrackReference>()
                : await GetTracksForPlaylistAsync(selectedPlaylistContext);
            visibleLibraryTracks = selectedPlaylistContext is null ? libraryTracks : selectedPlaylistTracks;
        }
        else if (!string.IsNullOrWhiteSpace(folderPath))
        {
            selectedFolderContext = folders.FirstOrDefault(folder => string.Equals(folder.Path, folderPath, StringComparison.OrdinalIgnoreCase));
            visibleLibraryTracks = selectedFolderContext is null
                ? libraryTracks
                : libraryTracks.Where(track => track.Path.StartsWith(selectedFolderContext.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            visibleLibraryTracks = libraryTracks;
            selectedPlaylistTracks = Array.Empty<TrackReference>();
        }

        RefreshLibraryList();
        RefreshFolderList();
        if (refreshPlaylists)
        {
            RefreshPlaylistList();
        }

        RefreshPlaylistTrackList();
        SyncTrackSelection(playbackOrchestrator.CurrentTrack);
        ConfigureLibraryAutoScan();
    }

    private void UpdatePositionUi()
    {
        var position = playbackOrchestrator.Position;
        var duration = playbackOrchestrator.Duration ?? TimeSpan.Zero;

        CurrentPositionTextBlock.Text = FormatTime(position);
        DurationTextBlock.Text = FormatTime(duration);

        PositionSlider.Maximum = 1;
        PositionSlider.Value = duration.TotalSeconds > 0
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
            : 0;

        smtcService?.UpdateTimeline(position, duration);
    }

    private void UpdateSmtcSession(PlaybackTransportState state, TrackReference? track)
    {
        if (smtcService is null)
        {
            return;
        }

        var queueCount = playbackOrchestrator.QueueController.Queue.Count;
        var canGoNext = queueCount > 1 || playbackOrchestrator.QueueController.RepeatMode != RepeatMode.Off;
        var canGoPrevious = queueCount > 1 || playbackOrchestrator.Position > TimeSpan.Zero;
        var duration = playbackOrchestrator.Duration ?? TimeSpan.Zero;

        smtcService.UpdateSession(
            track,
            state,
            playbackOrchestrator.Position,
            duration,
            canGoNext,
            canGoPrevious);
    }

    private void ApplyLibraryViewState(bool focusCurrentTrack = true)
    {
        if (LibraryTracksListBox is null)
        {
            filteredLibraryTracks = visibleLibraryTracks;
            return;
        }

        IEnumerable<TrackReference> query = visibleLibraryTracks;

        if (!string.IsNullOrWhiteSpace(libraryContextFilterType) && !string.IsNullOrWhiteSpace(libraryContextFilterValue))
        {
            var filterValue = libraryContextFilterValue.Trim();
            query = libraryContextFilterType.Trim().ToLowerInvariant() switch
            {
                "artist" => query.Where(track => string.Equals(track.Artist?.Trim(), filterValue, StringComparison.OrdinalIgnoreCase)),
                "album" => query.Where(track => string.Equals(track.Album?.Trim(), filterValue, StringComparison.OrdinalIgnoreCase)),
                "year" => query.Where(track => string.Equals(track.Year?.Trim(), filterValue, StringComparison.OrdinalIgnoreCase)),
                "genre" => query.Where(track => string.Equals(track.Genre?.Trim(), filterValue, StringComparison.OrdinalIgnoreCase)),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(librarySearchText))
        {
            var search = librarySearchText.Trim();
            query = query.Where(track =>
                track.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (track.Artist?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (track.Album?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (track.Year?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (track.Genre?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = libraryFilterMode switch
        {
            "withArtist" => query.Where(track => !string.IsNullOrWhiteSpace(track.Artist)),
            "unknownArtist" => query.Where(track => string.IsNullOrWhiteSpace(track.Artist)),
            _ => query
        };

        query = librarySortMode switch
        {
            "artist" => query.OrderBy(track => track.Artist ?? string.Empty).ThenBy(track => track.Title),
            "album" => query.OrderBy(track => track.Album ?? string.Empty).ThenBy(track => track.Title),
            "duration" => query.OrderByDescending(track => track.DurationSeconds).ThenBy(track => track.Title),
            _ => query.OrderBy(track => track.Title)
        };

        filteredLibraryTracks = query.ToArray();

        LibraryTracksListBox.ItemsSource = null;
        LibraryTracksListBox.ItemsSource = filteredLibraryTracks;

        if (focusCurrentTrack)
        {
            SyncTrackSelection(playbackOrchestrator.CurrentTrack);
            return;
        }

        LibraryTracksListBox.SelectedItem = null;

        if (filteredLibraryTracks.Count > 0)
        {
            LibraryTracksListBox.ScrollIntoView(filteredLibraryTracks[0]);
        }
    }

    private static string BuildTrackMeta(TrackReference track)
    {
        var artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
        var album = string.IsNullOrWhiteSpace(track.Album) ? "Unknown Album" : track.Album;
        return $"{artist} - {album}";
    }

    private static string FormatTime(TimeSpan time)
    {
        var clamped = time < TimeSpan.Zero ? TimeSpan.Zero : time;

        if (clamped.TotalHours >= 1)
        {
            return $"{(int)clamped.TotalHours:00}:{clamped.Minutes:00}:{clamped.Seconds:00}";
        }

        return $"{(int)clamped.TotalMinutes:00}:{clamped.Seconds:00}";
    }

    private void UpdateNowPlayingArtwork(TrackReference? track)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Path))
        {
            SetNowPlayingArtwork(null);
            return;
        }

        if (artworkCache.TryGetValue(track.Path, out var cachedArtwork))
        {
            SetNowPlayingArtwork(cachedArtwork);
            return;
        }

        var artwork = TryLoadArtwork(track.Path);
        artworkCache[track.Path] = artwork;
        SetNowPlayingArtwork(artwork);
    }

    private void SetNowPlayingArtwork(ImageSource? artwork)
    {
        NowPlayingArtworkImage.Source = artwork;
        NowPlayingArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        NowPlayingArtworkPlaceholderTextBlock.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowTrackNotification(TrackReference track)
    {
        if (trayIcon is null || !appSettings.ShowTrackNotifications)
        {
            return;
        }

        trayIcon.BalloonTipTitle = track.Title;
        trayIcon.BalloonTipText = BuildTrackMeta(track);
        trayIcon.ShowBalloonTip(1800);
    }

    private Task ProcessLastFmSnapshotAsync(PlaybackTransportState state, TrackReference? track)
    {
        return lastFmScrobbleService.HandlePlaybackSnapshotAsync(
            state,
            track,
            playbackOrchestrator.Position,
            playbackOrchestrator.Duration ?? TimeSpan.Zero,
            appSettings.LastFmScrobblingEnabled,
            lastFmApiKey,
            lastFmSharedSecret);
    }

    private void RefreshLyricsPanel(TrackReference? track)
    {
        if (track is null)
        {
            lastLyricsTrackPath = null;
            HomeLyricsSourceTextBlock.Text = "No track selected";
            HomeLyricsTextBlock.Text = "Play a track to view lyrics.";
            return;
        }

        if (string.Equals(lastLyricsTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lastLyricsTrackPath = track.Path;
        lyricsLoadCts?.Cancel();
        lyricsLoadCts?.Dispose();
        lyricsLoadCts = new CancellationTokenSource();

        HomeLyricsSourceTextBlock.Text = "Loading lyrics...";
        HomeLyricsTextBlock.Text = "Searching local and online sources...";
        _ = LoadLyricsForTrackAsync(track, lyricsLoadCts.Token);
    }

    private async Task LoadLyricsForTrackAsync(TrackReference track, CancellationToken cancellationToken)
    {
        try
        {
            var lyricsResult = await lyricsService.LoadLyricsAsync(track, appSettings.OnlineLyricsEnabled, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                !string.Equals(lastLyricsTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HomeLyricsSourceTextBlock.Text = lyricsResult.Source switch
            {
                LyricsSourceKind.Lrc => "Source: .lrc",
                LyricsSourceKind.Srt => "Source: .srt",
                LyricsSourceKind.Embedded => "Source: embedded tag",
                LyricsSourceKind.Online => "Source: LRCLIB",
                _ => "Source: unavailable"
            };

            HomeLyricsTextBlock.Text = string.IsNullOrWhiteSpace(lyricsResult.Text)
                ? "Lyrics not found for this track."
                : lyricsResult.Text;
        }
        catch (OperationCanceledException)
        {
            // no-op
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            HomeLyricsSourceTextBlock.Text = "Source: unavailable";
            HomeLyricsTextBlock.Text = "Could not load lyrics for this track.";
        }
    }

    private static ImageSource? TryLoadArtwork(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var firstPicture = file.Tag.Pictures?.FirstOrDefault();
            if (firstPicture is null || firstPicture.Data is null || firstPicture.Data.Count <= 0)
            {
                return null;
            }

            using var stream = new MemoryStream(firstPicture.Data.Data);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshEqualizerUi(EqualizerProfile profile)
    {
        isUpdatingEqualizerControls = true;

        try
        {
            UpdateEqualizerAvailability();

            EqualizerPreampSlider.Value = profile.PreampDb;
            EqualizerPreampValueTextBlock.Text = FormatDecibels(profile.PreampDb);

            for (var index = 0; index < equalizerBandSliders.Length; index++)
            {
                var gain = profile.Bands.ElementAtOrDefault(index)?.GainDb ?? 0;
                equalizerBandSliders[index].Value = gain;
                equalizerBandValueTextBlocks[index].Text = FormatDecibels(gain);
            }

            RefreshEqualizerButtonState(profile);
            SyncSelectedPreset(profile);
            SyncSettingsEffectControls();
        }
        finally
        {
            isUpdatingEqualizerControls = false;
        }
    }

    private void RefreshDspUi(DspProfile profile)
    {
        isUpdatingDspControls = true;

        try
        {
            LoudnessGainSlider.Value = profile.LoudnessGainDb;
            LoudnessGainValueTextBlock.Text = FormatDecibels(profile.LoudnessGainDb);
            LoudnessToggleButton.Content = profile.LoudnessEnabled ? "Loudness On" : "Loudness Off";
            LoudnessToggleButton.Tag = profile.LoudnessEnabled ? "Active" : null;
            ReplayGainToggleButton.Content = profile.ReplayGainEnabled ? "ReplayGain On" : "ReplayGain Off";
            ReplayGainToggleButton.Tag = profile.ReplayGainEnabled ? "Active" : null;
            SyncSettingsEffectControls();
        }
        finally
        {
            isUpdatingDspControls = false;
        }
    }

    private void ApplyEqualizerProfile(EqualizerProfile profile, bool persist = true, bool refreshUi = true)
    {
        playbackOrchestrator.Effects.ApplyEqualizer(profile);

        if (refreshUi)
        {
            RefreshEqualizerUi(profile);
        }

        if (persist)
        {
            _ = SaveEqualizerProfileAsync(profile);
        }
    }

    private void ApplyDspProfile(DspProfile profile, bool persist = true, bool refreshUi = true)
    {
        playbackOrchestrator.Effects.ApplyDspProfile(profile);
        var effectiveProfile = playbackOrchestrator.Effects.DspProfile;

        if (refreshUi)
        {
            RefreshDspUi(effectiveProfile);
        }

        if (persist)
        {
            _ = SaveDspProfileAsync(effectiveProfile);
        }
    }

    private void UpdateEqualizerAvailability()
    {
        var isAvailable = playbackOrchestrator.Effects.IsAvailable && playbackOrchestrator.BackendCapabilities.SupportsEqualizer;
        EqualizerStatusTextBlock.Text = isAvailable
            ? $"Live on {playbackOrchestrator.BackendDisplayName}"
            : "Unavailable";

        EqualizerToggleButton.IsEnabled = isAvailable;
        EqualizerResetButton.IsEnabled = isAvailable;
        EqualizerPreampSlider.IsEnabled = isAvailable;
        LoudnessToggleButton.IsEnabled = isAvailable && playbackOrchestrator.BackendCapabilities.SupportsDspEffects;
        ReplayGainToggleButton.IsEnabled = isAvailable && playbackOrchestrator.BackendCapabilities.SupportsReplayGain;
        LoudnessGainSlider.IsEnabled = isAvailable && playbackOrchestrator.BackendCapabilities.SupportsDspEffects;

        foreach (var slider in equalizerBandSliders)
        {
            slider.IsEnabled = isAvailable;
        }

        RefreshSettingsControlAvailability();
    }

    private void RefreshEqualizerButtonState(EqualizerProfile profile)
    {
        EqualizerToggleButton.Content = profile.IsEnabled ? "EQ On" : "EQ Off";
        EqualizerToggleButton.Tag = profile.IsEnabled ? "Active" : null;
        EqualizerPreampValueTextBlock.Text = FormatDecibels(EqualizerPreampSlider.Value);
        UpdateEqualizerValueLabels();
        DeleteEqualizerPresetButton.IsEnabled =
            EqualizerPresetComboBox.SelectedItem is NamedEqualizerPreset selectedPreset &&
            !selectedPreset.IsBuiltIn;
    }

    private void UpdateEqualizerValueLabels()
    {
        for (var index = 0; index < equalizerBandSliders.Length; index++)
        {
            equalizerBandValueTextBlocks[index].Text = FormatDecibels(equalizerBandSliders[index].Value);
        }
    }

    private static string FormatDecibels(double value)
    {
        return $"{value:+0.0;-0.0;0.0} dB";
    }

    private async Task SaveEqualizerProfileAsync(EqualizerProfile profile)
    {
        try
        {
            await equalizerProfileStore.SaveAsync(profile);
        }
        catch
        {
            // Keep playback responsive even if local EQ persistence fails.
        }
    }

    private async Task SaveDspProfileAsync(DspProfile profile)
    {
        try
        {
            await dspProfileStore.SaveAsync(profile);
        }
        catch
        {
            // Keep playback responsive even if local DSP persistence fails.
        }
    }

    private async Task<AppSettingsProfile> LoadAppSettingsAsync()
    {
        try
        {
            var loaded = await appSettingsStore.LoadAsync();
            return NormalizeAppSettings(loaded);
        }
        catch
        {
            return AppSettingsProfile.Default;
        }
    }

    private void ApplySettingsToUi()
    {
        isLoadingSettingsControls = true;

        try
        {
            StartMinimizedCheckBox.IsChecked = appSettings.StartMinimizedToTray;
            RestoreSessionCheckBox.IsChecked = appSettings.RestoreLastSession;
            AutoPlayAfterRestoreCheckBox.IsChecked = appSettings.AutoPlayAfterRestore;
            MinimizeToTrayOnCloseCheckBox.IsChecked = appSettings.MinimizeToTrayOnClose;
            TrackNotificationsCheckBox.IsChecked = appSettings.ShowTrackNotifications;
            AutoScanLibraryCheckBox.IsChecked = appSettings.AutoScanLibraryEnabled;
            RememberLastSectionCheckBox.IsChecked = appSettings.RememberLastSection;
            SettingsOnlineLyricsEnabledCheckBox.IsChecked = appSettings.OnlineLyricsEnabled;
            SettingsLastFmScrobblingEnabledCheckBox.IsChecked = hasLastFmBuildCredentials && appSettings.LastFmScrobblingEnabled;

            SelectComboItemByTag(SettingsStartupSectionComboBox, NormalizeSection(appSettings.StartupSection));

            SyncLibraryViewControls();
            SyncSettingsEffectControls();
            RefreshSettingsControlAvailability();
            RefreshLastFmUi();
        }
        finally
        {
            isLoadingSettingsControls = false;
        }
    }

    private void RefreshSettingsControlAvailability()
    {
        if (!IsLoaded)
        {
            return;
        }

        var rememberLastSection = RememberLastSectionCheckBox.IsChecked == true;

        AutoPlayAfterRestoreCheckBox.IsEnabled = RestoreSessionCheckBox.IsChecked == true;
        SettingsStartupSectionComboBox.IsEnabled = !rememberLastSection;
        SettingsStartupSectionHintTextBlock.Text = rememberLastSection
            ? "Startup section is ignored while 'Remember last opened section' is enabled."
            : "Applies on next full app launch (use Exit from tray).";
        SettingsEqEnabledCheckBox.IsEnabled = playbackOrchestrator.Effects.IsAvailable && playbackOrchestrator.BackendCapabilities.SupportsEqualizer;
        SettingsLoudnessEnabledCheckBox.IsEnabled = playbackOrchestrator.BackendCapabilities.SupportsDspEffects;
        SettingsReplayGainEnabledCheckBox.IsEnabled = playbackOrchestrator.BackendCapabilities.SupportsReplayGain;
        SettingsLastFmScrobblingEnabledCheckBox.IsEnabled = hasLastFmBuildCredentials;
        RefreshLastFmUi();
    }

    private void RefreshLastFmUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var isConnected = lastFmScrobbleService.IsConnected;
        SettingsLastFmCredentialsHintTextBlock.Text = hasLastFmBuildCredentials
            ? "Last.fm credentials are bundled with this Petrichor build (macOS-style setup)."
            : "Last.fm credentials are not bundled in this build. Add Windows/Configuration/Secrets.props and rebuild.";

        var displayName = !hasLastFmBuildCredentials
            ? "Built-in Last.fm credentials are not configured."
            : isConnected
                ? string.IsNullOrWhiteSpace(lastFmScrobbleService.Username)
                    ? "Connected"
                    : $"Connected as {lastFmScrobbleService.Username}"
                : "Not connected";

        SettingsLastFmStatusTextBlock.Text = displayName;
        SettingsLastFmConnectButton.IsEnabled = hasLastFmBuildCredentials && !isConnected;
        SettingsLastFmDisconnectButton.IsEnabled = isConnected;
    }

    private async Task TrySaveAppSettingsAsync(AppSettingsProfile profile)
    {
        try
        {
            await appSettingsStore.SaveAsync(profile);
        }
        catch
        {
            // Keep UI responsive if settings persistence fails.
        }
    }

    private void SyncSettingsEffectControls()
    {
        isLoadingSettingsControls = true;

        try
        {
            SettingsEqEnabledCheckBox.IsChecked = playbackOrchestrator.Effects.EqualizerProfile.IsEnabled;
            SettingsLoudnessEnabledCheckBox.IsChecked = playbackOrchestrator.Effects.DspProfile.LoudnessEnabled;
            SettingsReplayGainEnabledCheckBox.IsChecked = playbackOrchestrator.Effects.DspProfile.ReplayGainEnabled;
        }
        finally
        {
            isLoadingSettingsControls = false;
        }
    }

    private void SyncLibraryViewControls()
    {
        isSyncingLibraryOptions = true;

        try
        {
            SelectComboItemByTag(LibrarySortComboBox, librarySortMode);
            SelectComboItemByTag(LibraryFilterComboBox, libraryFilterMode);
        }
        finally
        {
            isSyncingLibraryOptions = false;
        }
    }

    private static void SelectComboItemByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        var normalizedTag = (tag ?? string.Empty).Trim();

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemTag &&
                string.Equals(itemTag, normalizedTag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static AppSettingsProfile NormalizeAppSettings(AppSettingsProfile profile)
    {
        var normalizedRestoreSession = profile.RestoreLastSession;

        return profile with
        {
            AutoPlayAfterRestore = normalizedRestoreSession && profile.AutoPlayAfterRestore,
            StartupSection = NormalizeSection(profile.StartupSection),
            LastActiveSection = NormalizeSection(profile.LastActiveSection),
            LibrarySortMode = NormalizeLibrarySortMode(profile.LibrarySortMode),
            LibraryFilterMode = NormalizeLibraryFilterMode(profile.LibraryFilterMode),
            LastFmUsername = profile.LastFmUsername?.Trim() ?? string.Empty
        };
    }

    private static string ResolveInitialSection(AppSettingsProfile profile)
    {
        if (profile.RememberLastSection)
        {
            return NormalizeSection(profile.LastActiveSection);
        }

        return NormalizeSection(profile.StartupSection);
    }

    private static string NormalizeSection(string? section)
    {
        if (string.Equals(section, HomeSection, StringComparison.OrdinalIgnoreCase))
        {
            return HomeSection;
        }

        if (string.Equals(section, LibrarySection, StringComparison.OrdinalIgnoreCase))
        {
            return LibrarySection;
        }

        if (string.Equals(section, PlaylistsSection, StringComparison.OrdinalIgnoreCase))
        {
            return PlaylistsSection;
        }

        if (string.Equals(section, FoldersSection, StringComparison.OrdinalIgnoreCase))
        {
            return FoldersSection;
        }

        if (string.Equals(section, SettingsSection, StringComparison.OrdinalIgnoreCase))
        {
            return SettingsSection;
        }

        return LibrarySection;
    }

    private static string NormalizeLibrarySortMode(string? sortMode)
    {
        if (string.Equals(sortMode, "artist", StringComparison.OrdinalIgnoreCase))
        {
            return "artist";
        }

        if (string.Equals(sortMode, "album", StringComparison.OrdinalIgnoreCase))
        {
            return "album";
        }

        if (string.Equals(sortMode, "duration", StringComparison.OrdinalIgnoreCase))
        {
            return "duration";
        }

        return "title";
    }

    private static string NormalizeLibraryFilterMode(string? filterMode)
    {
        if (string.Equals(filterMode, "withArtist", StringComparison.OrdinalIgnoreCase))
        {
            return "withArtist";
        }

        if (string.Equals(filterMode, "unknownArtist", StringComparison.OrdinalIgnoreCase))
        {
            return "unknownArtist";
        }

        return "all";
    }

    private async Task<IReadOnlyList<NamedEqualizerPreset>> LoadEqualizerPresetsAsync()
    {
        var customPresets = await equalizerPresetStore.LoadAsync();
        return EqualizerPresets.BuiltIn.Concat(customPresets.Where(preset => !preset.IsBuiltIn)).ToArray();
    }

    private void RefreshEqualizerPresetList(string? preferredPresetName = null)
    {
        isSyncingPresetSelection = true;

        try
        {
            EqualizerPresetComboBox.ItemsSource = null;
            EqualizerPresetComboBox.ItemsSource = equalizerPresets;

            if (!string.IsNullOrWhiteSpace(preferredPresetName))
            {
                EqualizerPresetComboBox.SelectedItem = equalizerPresets.FirstOrDefault(
                    preset => string.Equals(preset.Name, preferredPresetName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SyncSelectedPreset(playbackOrchestrator.Effects.EqualizerProfile);
            }
        }
        finally
        {
            isSyncingPresetSelection = false;
        }

        DeleteEqualizerPresetButton.IsEnabled =
            EqualizerPresetComboBox.SelectedItem is NamedEqualizerPreset selectedPreset &&
            !selectedPreset.IsBuiltIn;
    }

    private void SyncSelectedPreset(EqualizerProfile profile)
    {
        isSyncingPresetSelection = true;

        try
        {
            EqualizerPresetComboBox.SelectedItem = equalizerPresets.FirstOrDefault(preset => ProfilesMatch(preset.Profile, profile));
        }
        finally
        {
            isSyncingPresetSelection = false;
        }
    }

    private string BuildSuggestedPresetName()
    {
        var timestamp = DateTime.Now.ToString("MM-dd HH-mm");
        return $"Custom EQ {timestamp}";
    }

    private TrackReference? ResolvePlaylistTrackDropTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem item && item.DataContext is TrackReference track)
            {
                return track;
            }

            source = GetParentDependencyObject(source);
        }

        return null;
    }

    private static DependencyObject? GetParentDependencyObject(DependencyObject source)
    {
        if (source is Visual)
        {
            return VisualTreeHelper.GetParent(source);
        }

        if (source.GetType().FullName == "System.Windows.Media.Media3D.Visual3D")
        {
            return VisualTreeHelper.GetParent(source);
        }

        if (source is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (source is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement) as DependencyObject
                ?? LogicalTreeHelper.GetParent(contentElement) as DependencyObject;
        }

        return LogicalTreeHelper.GetParent(source);
    }

    private static bool TracksMatch(TrackReference left, TrackReference right)
    {
        return left.TrackId is not null && right.TrackId is not null
            ? left.TrackId == right.TrackId
            : string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProfilesMatch(EqualizerProfile left, EqualizerProfile right)
    {
        if (left.IsEnabled != right.IsEnabled || Math.Abs(left.PreampDb - right.PreampDb) > 0.01 || left.Bands.Count != right.Bands.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Bands.Count; index++)
        {
            var leftBand = left.Bands[index];
            var rightBand = right.Bands[index];

            if (Math.Abs(leftBand.FrequencyHz - rightBand.FrequencyHz) > 0.01 ||
                Math.Abs(leftBand.GainDb - rightBand.GainDb) > 0.01 ||
                Math.Abs(leftBand.Q - rightBand.Q) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "eq-preset" : sanitized;
    }

    private sealed record ImportedEqualizerPreset(string Name, EqualizerProfile Profile);

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T match)
            {
                return match;
            }

            var nestedMatch = FindDescendant<T>(child);
            if (nestedMatch is not null)
            {
                return nestedMatch;
            }
        }

        return null;
    }

    private TrackReference? ResolveTrackFromMenu(object sender)
    {
        return TryResolveCommandParameter<TrackReference>(sender, out var track)
            ? track
            : LibraryTracksListBox.SelectedItem as TrackReference;
    }

    private static bool TryResolveCommandParameter<T>(object sender, out T? value)
        where T : class
    {
        if (sender is System.Windows.Controls.MenuItem { CommandParameter: T commandParameter })
        {
            value = commandParameter;
            return true;
        }

        value = null;
        return false;
    }

    private void ApplyLibraryFacetFilter(string filterType, string filterValue, string label)
    {
        libraryContextFilterType = filterType;
        libraryContextFilterValue = filterValue;
        librarySearchText = string.Empty;
        libraryFilterMode = "all";
        selectedPlaylistContext = null;
        selectedFolderContext = null;
        selectedPlaylistTracks = Array.Empty<TrackReference>();
        PlaylistsListBox.SelectedItem = null;
        PlaylistsCenterListBox.SelectedItem = null;
        FoldersListBox.SelectedItem = null;
        FoldersCenterListBox.SelectedItem = null;
        visibleLibraryTracks = libraryTracks;

        LibrarySearchTextBox.Text = string.Empty;
        SyncLibraryViewControls();
        SetActiveSection(LibrarySection);
        RefreshLibraryList();
        RefreshPlaylistTrackList();
        SyncTrackSelection(playbackOrchestrator.CurrentTrack);

        LibraryCenterSummaryTextBlock.Text = $"Filtered by {label}: {filterValue}";
    }

    private async Task PinLibraryFacetAsync(string filterType, string filterValue, string displayName, string iconName)
    {
        var alreadyPinned = pinnedItems.Any(item =>
            string.Equals(item.ItemType, "filter", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.FilterType, filterType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.FilterValue, filterValue, StringComparison.OrdinalIgnoreCase));
        if (alreadyPinned)
        {
            return;
        }

        await pinnedItemRepository.UpsertAsync(new PinnedItemReference(
            Id: null,
            ItemType: "filter",
            FilterType: filterType,
            FilterValue: filterValue,
            EntityId: null,
            PlaylistId: null,
            DisplayName: displayName,
            IconName: iconName,
            SortOrder: pinnedItems.Count));
        pinnedItems = await pinnedItemRepository.GetAllAsync();
        RefreshPinnedItemList();
    }

    private async Task NavigateFromPinnedItemAsync(PinnedItemReference pinnedItem)
    {
        if (string.Equals(pinnedItem.ItemType, "playlist", StringComparison.OrdinalIgnoreCase) && pinnedItem.PlaylistId is Guid playlistId)
        {
            var playlist = playlists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null)
            {
                await ReloadPlaylistsAsync(playlistId);
                playlist = playlists.FirstOrDefault(item => item.Id == playlistId);
            }

            if (playlist is not null)
            {
                PlaylistsListBox.SelectedItem = playlist;
                PlaylistsCenterListBox.SelectedItem = playlist;
                await ApplyPlaylistContextAsync(playlist);
                SetActiveSection(PlaylistsSection);
            }

            return;
        }

        if (string.Equals(pinnedItem.ItemType, "filter", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(pinnedItem.FilterType) &&
            !string.IsNullOrWhiteSpace(pinnedItem.FilterValue))
        {
            ApplyLibraryFacetFilter(pinnedItem.FilterType, pinnedItem.FilterValue, "Pinned");
        }
    }

    private string BuildSmartPlaylistCriteriaFromCurrentLibraryView()
    {
        var rules = new List<object>();

        if (!string.IsNullOrWhiteSpace(libraryContextFilterType) && !string.IsNullOrWhiteSpace(libraryContextFilterValue))
        {
            rules.Add(new
            {
                field = libraryContextFilterType,
                condition = "equals",
                value = libraryContextFilterValue
            });
        }

        if (!string.IsNullOrWhiteSpace(librarySearchText))
        {
            rules.Add(new
            {
                field = "anyText",
                condition = "contains",
                value = librarySearchText.Trim()
            });
        }

        if (string.Equals(libraryFilterMode, "withArtist", StringComparison.OrdinalIgnoreCase))
        {
            rules.Add(new
            {
                field = "hasArtist",
                condition = "equals",
                value = "true"
            });
        }
        else if (string.Equals(libraryFilterMode, "unknownArtist", StringComparison.OrdinalIgnoreCase))
        {
            rules.Add(new
            {
                field = "hasArtist",
                condition = "equals",
                value = "false"
            });
        }

        var payload = new
        {
            rules,
            matchMode = "all",
            sortBy = librarySortMode,
            sortDescending = string.Equals(librarySortMode, "duration", StringComparison.OrdinalIgnoreCase),
            limit = 0
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<string> ParseM3uEntries(string playlistPath)
    {
        if (!File.Exists(playlistPath))
        {
            return Array.Empty<string>();
        }

        var lines = File.ReadAllLines(playlistPath);
        return lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToArray();
    }

    private static string? ResolveM3uPath(string playlistPath, string rawEntry)
    {
        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return null;
        }

        var entry = rawEntry.Trim();
        if (Uri.TryCreate(entry, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                return Path.GetFullPath(uri.LocalPath);
            }

            return null;
        }

        if (Path.IsPathRooted(entry))
        {
            return Path.GetFullPath(entry);
        }

        var playlistDirectory = Path.GetDirectoryName(playlistPath);
        if (string.IsNullOrWhiteSpace(playlistDirectory))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(playlistDirectory, entry));
    }

    private async Task RescanAllFoldersAsync(bool showSummary)
    {
        var knownFolders = await folderRepository.GetAllAsync();
        if (knownFolders.Count == 0)
        {
            if (showSummary)
            {
                ShowStyledInfoDialog("Rescan Library", "No folders are currently mapped.");
            }

            return;
        }

        var rescannedFolders = 0;
        var removedMissingFolders = 0;
        foreach (var folder in knownFolders)
        {
            if (!Directory.Exists(folder.Path))
            {
                await folderRepository.DeleteByPathAsync(folder.Path);
                removedMissingFolders++;
                continue;
            }

            await libraryImportService.ImportFolderAsync(folder.Path);
            rescannedFolders++;
        }

        await RefreshCollectionsAsync(refreshPlaylists: true);
        SetActiveSection(LibrarySection);

        if (showSummary)
        {
            ShowStyledInfoDialog(
                "Rescan Library",
                $"Rescanned folders: {rescannedFolders}. Removed missing folders: {removedMissingFolders}.");
        }
    }

    private string BuildPlaylistName()
    {
        var queueSource = playbackOrchestrator.QueueController.QueueSource;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm");

        return queueSource switch
        {
            QueueSource.Folder => $"Folder Queue {timestamp}",
            QueueSource.Playlist => $"Playlist Snapshot {timestamp}",
            _ => $"Library Queue {timestamp}"
        };
    }

    private PlaylistReference? GetSelectedPlaylist()
    {
        return PlaylistsCenterListBox.SelectedItem as PlaylistReference
            ?? PlaylistsListBox.SelectedItem as PlaylistReference;
    }

    private PlaylistReference? ResolvePlaylistFromSender(object sender)
    {
        if (TryResolveCommandParameter<PlaylistReference>(sender, out var playlist))
        {
            return playlist;
        }

        return GetSelectedPlaylist();
    }

    private async Task<IReadOnlyList<TrackReference>> GetTracksForPlaylistAsync(PlaylistReference playlist, CancellationToken cancellationToken = default)
    {
        if (string.Equals(playlist.Type, "smart", StringComparison.OrdinalIgnoreCase))
        {
            return await trackRepository.GetBySmartCriteriaAsync(playlist.SmartCriteriaJson ?? string.Empty, cancellationToken);
        }

        return await playlistTrackRepository.GetTracksForPlaylistAsync(playlist.Id, cancellationToken);
    }

    private async Task<IReadOnlyList<PlaylistReference>> LoadPlaylistsWithSmartCountsAsync(CancellationToken cancellationToken = default)
    {
        var basePlaylists = await playlistRepository.GetAllAsync(cancellationToken);
        if (basePlaylists.Count == 0)
        {
            return basePlaylists;
        }

        var enriched = new List<PlaylistReference>(basePlaylists.Count);
        foreach (var playlist in basePlaylists)
        {
            if (!string.Equals(playlist.Type, "smart", StringComparison.OrdinalIgnoreCase))
            {
                enriched.Add(playlist);
                continue;
            }

            var smartTracks = await trackRepository.GetBySmartCriteriaAsync(playlist.SmartCriteriaJson ?? string.Empty, cancellationToken);
            enriched.Add(playlist with { TrackCount = smartTracks.Count });
        }

        return enriched;
    }

    private async Task ReloadPlaylistsAsync(Guid? selectedPlaylistId = null)
    {
        playlists = await LoadPlaylistsWithSmartCountsAsync();
        RefreshPlaylistList();

        if (selectedPlaylistId is not null)
        {
            var selectedPlaylist = playlists.FirstOrDefault(playlist => playlist.Id == selectedPlaylistId.Value);
            PlaylistsListBox.SelectedItem = selectedPlaylist;
            PlaylistsCenterListBox.SelectedItem = selectedPlaylist;
            await ApplyPlaylistContextAsync(selectedPlaylist);
        }
        else
        {
            PlaylistsListBox.SelectedItem = null;
            PlaylistsCenterListBox.SelectedItem = null;
            await ApplyPlaylistContextAsync(null);
        }
    }

    private async Task ApplyPlaylistContextAsync(PlaylistReference? playlist)
    {
        selectedPlaylistContext = playlist;
        selectedFolderContext = null;
        libraryContextFilterType = null;
        libraryContextFilterValue = null;
        FoldersListBox.SelectedItem = null;
        FoldersCenterListBox.SelectedItem = null;
        selectedPlaylistTracks = playlist is null
            ? Array.Empty<TrackReference>()
            : await GetTracksForPlaylistAsync(playlist);
        visibleLibraryTracks = playlist is null
            ? libraryTracks
            : selectedPlaylistTracks;

        RefreshLibraryList();
        RefreshPlaylistTrackList();
        SyncTrackSelection(playbackOrchestrator.CurrentTrack);
    }

    private async Task ApplyFolderContextAsync(FolderReference? folder)
    {
        selectedFolderContext = folder;
        selectedPlaylistContext = null;
        libraryContextFilterType = null;
        libraryContextFilterValue = null;
        selectedPlaylistTracks = Array.Empty<TrackReference>();
        PlaylistsListBox.SelectedItem = null;
        PlaylistsCenterListBox.SelectedItem = null;

        visibleLibraryTracks = folder is null
            ? libraryTracks
            : (await libraryImportService.GetLibraryTracksAsync())
                .Where(track => track.Path.StartsWith(folder.Path, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        RefreshLibraryList();
        RefreshPlaylistTrackList();
        SyncTrackSelection(playbackOrchestrator.CurrentTrack);
    }

    private string BuildVisibleLibrarySummary()
    {
        if (visibleLibraryTracks.Count == 0)
        {
            return selectedPlaylistContext is not null
                ? $"Playlist '{selectedPlaylistContext.Name}' is empty"
                : selectedFolderContext is not null
                    ? $"Folder '{selectedFolderContext.Name}' is empty"
                    : !string.IsNullOrWhiteSpace(libraryContextFilterType) && !string.IsNullOrWhiteSpace(libraryContextFilterValue)
                        ? $"No tracks match {libraryContextFilterType}: '{libraryContextFilterValue}'"
                        : "No library folders added";
        }

        return selectedPlaylistContext is not null
            ? $"{visibleLibraryTracks.Count} tracks in playlist '{selectedPlaylistContext.Name}'"
            : selectedFolderContext is not null
                ? $"{visibleLibraryTracks.Count} tracks in folder '{selectedFolderContext.Name}'"
                : !string.IsNullOrWhiteSpace(libraryContextFilterType) && !string.IsNullOrWhiteSpace(libraryContextFilterValue)
                    ? $"{visibleLibraryTracks.Count} tracks for {libraryContextFilterType}: '{libraryContextFilterValue}'"
                    : $"{visibleLibraryTracks.Count} tracks in library";
    }

    private async Task<PlaylistReference> CreatePlaylistAsync(
        string playlistName,
        int trackCount,
        string playlistType = "manual",
        bool isUserEditable = true,
        bool isContentEditable = true,
        string? smartCriteriaJson = null)
    {
        var existingPlaylists = await playlistRepository.GetAllAsync();
        var playlist = new PlaylistReference(
            Id: Guid.NewGuid(),
            Name: playlistName,
            Type: playlistType,
            IsUserEditable: isUserEditable,
            IsContentEditable: isContentEditable,
            TrackCount: trackCount,
            SortOrder: existingPlaylists.Count == 0 ? 0 : existingPlaylists.Max(item => item.SortOrder) + 1,
            SmartCriteriaJson: smartCriteriaJson);

        await playlistRepository.UpsertAsync(playlist);
        return playlist;
    }

    private async Task SavePlaylistTrackOrderAsync(PlaylistReference playlist, IReadOnlyList<TrackReference> tracks, bool reloadPlaylists)
    {
        var orderedTrackIds = tracks
            .Where(track => track.TrackId.HasValue)
            .Select(track => track.TrackId!.Value)
            .ToArray();

        await playlistTrackRepository.ReplaceTracksAsync(playlist.Id, orderedTrackIds);
        selectedPlaylistTracks = tracks;
        visibleLibraryTracks = tracks;
        RefreshPlaylistTrackList();
        RefreshLibraryList();

        if (reloadPlaylists)
        {
            await ReloadPlaylistsAsync(playlist.Id);
        }
    }

    private string? PromptForText(string title, string message, string defaultValue)
    {
        var dialog = CreateStyledDialogWindow(title);

        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            MinWidth = 320,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#101418")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E4253")),
            CaretBrush = System.Windows.Media.Brushes.White
        };

        var okButton = CreateDialogButton("Save", isPrimary: true);
        okButton.Margin = new Thickness(0, 16, 10, 0);
        okButton.IsDefault = true;

        var cancelButton = CreateDialogButton("Cancel");
        cancelButton.Margin = new Thickness(0, 16, 0, 0);
        cancelButton.IsCancel = true;

        okButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        dialog.Content = CreateDialogContent(
            title,
            message,
            input,
            new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children =
                {
                    okButton,
                    cancelButton
                }
            });

        dialog.Loaded += (_, _) =>
        {
            input.SelectAll();
            input.Focus();
        };

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void ShowStyledInfoDialog(string title, string message)
    {
        var dialog = CreateStyledDialogWindow(title);
        var okButton = CreateDialogButton("OK", isPrimary: true);
        okButton.IsDefault = true;
        okButton.Click += (_, _) => dialog.DialogResult = true;

        dialog.Content = CreateDialogContent(
            title,
            message,
            null,
            new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children = { okButton }
            });

        dialog.ShowDialog();
    }

    private bool ShowStyledConfirmationDialog(string title, string message, string confirmLabel, bool isDestructive = false)
    {
        var dialog = CreateStyledDialogWindow(title);
        var confirmButton = CreateDialogButton(confirmLabel, isPrimary: !isDestructive, isDestructive: isDestructive);
        confirmButton.Margin = new Thickness(0, 0, 10, 0);
        confirmButton.IsDefault = true;

        var cancelButton = CreateDialogButton("Cancel");
        cancelButton.IsCancel = true;

        confirmButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        dialog.Content = CreateDialogContent(
            title,
            message,
            null,
            new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children =
                {
                    confirmButton,
                    cancelButton
                }
            });

        return dialog.ShowDialog() == true;
    }

    private Window CreateStyledDialogWindow(string title)
    {
        return new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false
        };
    }

    private FrameworkElement CreateDialogContent(
        string title,
        string message,
        UIElement? body,
        UIElement actions)
    {
        return new Border
        {
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#151B21")),
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#24313D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = message,
                        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9DB0C4")),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, body is null ? 18 : 12),
                        MaxWidth = 380
                    },
                    body ?? new Border { Height = 0 },
                    new Border
                    {
                        Height = 16,
                        Opacity = 0
                    },
                    actions
                }
            }
        };
    }

    private System.Windows.Controls.Button CreateDialogButton(string label, bool isPrimary = false, bool isDestructive = false)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = label,
            Width = 92,
            Height = 36,
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = (Style)FindResource(
                isDestructive || isPrimary
                    ? "SidebarActionButtonStyle"
                    : "TransportButtonStyle")
        };

        if (isDestructive)
        {
            button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C84F63"));
            button.Foreground = System.Windows.Media.Brushes.White;
        }

        return button;
    }

    private void SyncTrackSelection(TrackReference? currentTrack)
    {
        RefreshHomeLibraryPreview(currentTrack);

        var matchedTrack = FindMatchingTrack(currentTrack);

        LibraryTracksListBox.SelectedItem = matchedTrack;
        HomeLibraryPreviewListBox.SelectedItem = matchedTrack;

        if (matchedTrack is not null)
        {
            LibraryTracksListBox.ScrollIntoView(matchedTrack);
            HomeLibraryPreviewListBox.ScrollIntoView(matchedTrack);
        }
    }

    private void RefreshHomeLibraryPreview(TrackReference? currentTrack)
    {
        homePreviewTracks = BuildHomePreviewTracks(currentTrack);

        HomeLibraryPreviewListBox.ItemsSource = null;
        HomeLibraryPreviewListBox.ItemsSource = homePreviewTracks;
        HomeLibrarySummaryTextBlock.Text = visibleLibraryTracks.Count == 0
            ? "No tracks available"
            : selectedPlaylistContext is null
                ? selectedFolderContext is null
                    ? $"{homePreviewTracks.Count} of {visibleLibraryTracks.Count} tracks in view"
                    : $"{homePreviewTracks.Count} of {visibleLibraryTracks.Count} tracks from folder '{selectedFolderContext.Name}'"
                : $"{homePreviewTracks.Count} of {visibleLibraryTracks.Count} tracks from '{selectedPlaylistContext.Name}'";
    }

    private IReadOnlyList<TrackReference> BuildHomePreviewTracks(TrackReference? currentTrack)
    {
        if (visibleLibraryTracks.Count == 0)
        {
            return Array.Empty<TrackReference>();
        }

        var matchedTrack = FindMatchingTrack(currentTrack);
        if (matchedTrack is null)
        {
            return visibleLibraryTracks.Take(3).ToArray();
        }

        var currentIndex = visibleLibraryTracks
            .Select((track, index) => new { track, index })
            .FirstOrDefault(item => ReferenceEquals(item.track, matchedTrack))?.index ?? -1;
        if (currentIndex < 0)
        {
            return visibleLibraryTracks.Take(3).ToArray();
        }

        var startIndex = Math.Max(0, currentIndex - 1);
        if (startIndex + 3 > visibleLibraryTracks.Count)
        {
            startIndex = Math.Max(0, visibleLibraryTracks.Count - 3);
        }

        return visibleLibraryTracks.Skip(startIndex).Take(3).ToArray();
    }

    private TrackReference? FindMatchingTrack(TrackReference? currentTrack)
    {
        if (currentTrack is null)
        {
            return null;
        }

        return visibleLibraryTracks.FirstOrDefault(track =>
            track.TrackId == currentTrack.TrackId && track.TrackId is not null ||
            string.Equals(track.Path, currentTrack.Path, StringComparison.OrdinalIgnoreCase));
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        MaximizeGlyphTextBlock.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }
    private void SetActiveSection(string section)
    {
        var normalizedSection = NormalizeSection(section);
        activeSection = normalizedSection;

        HomeView.Visibility = normalizedSection == HomeSection ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = normalizedSection == LibrarySection ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsView.Visibility = normalizedSection == PlaylistsSection ? Visibility.Visible : Visibility.Collapsed;
        FoldersView.Visibility = normalizedSection == FoldersSection ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = normalizedSection == SettingsSection ? Visibility.Visible : Visibility.Collapsed;

        SetSidebarState(HomeSidebarItem, normalizedSection == HomeSection);
        SetSidebarState(LibrarySidebarItem, normalizedSection == LibrarySection);
        SetSidebarState(PlaylistsSidebarItem, normalizedSection == PlaylistsSection);
        SetSidebarState(FoldersSidebarItem, normalizedSection == FoldersSection);
        SetSidebarState(SettingsSidebarItem, normalizedSection == SettingsSection);

        if (IsLoaded && appSettings.RememberLastSection && !string.Equals(appSettings.LastActiveSection, normalizedSection, StringComparison.Ordinal))
        {
            appSettings = appSettings with
            {
                LastActiveSection = normalizedSection
            };
            _ = TrySaveAppSettingsAsync(appSettings);
        }

        if (normalizedSection == LibrarySection || normalizedSection == HomeSection)
        {
            Dispatcher.BeginInvoke(
                () => SyncTrackSelection(playbackOrchestrator.CurrentTrack),
                DispatcherPriority.Background);
        }
    }

    private static void SetSidebarState(Border border, bool isActive)
    {
        border.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isActive ? "#18232C" : "Transparent"));
        border.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isActive ? "#284555" : "Transparent"));
    }

    private sealed class PetrichorTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly System.Drawing.Color NormalBackground = System.Drawing.Color.FromArgb(15, 21, 27);
        private static readonly System.Drawing.Color HoverBackground = System.Drawing.Color.FromArgb(33, 39, 46);
        private static readonly System.Drawing.Color PressedBackground = System.Drawing.Color.FromArgb(41, 48, 57);

        public PetrichorTrayMenuRenderer()
            : base(new PetrichorTrayColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var bounds = new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size);
            var background = e.Item.Pressed
                ? PressedBackground
                : e.Item.Selected
                    ? HoverBackground
                    : NormalBackground;

            using var brush = new System.Drawing.SolidBrush(background);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(33, 39, 46));
            var borderBounds = new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, borderBounds);
        }
    }

    private sealed class PetrichorTrayColorTable : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(15, 21, 27);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(33, 39, 46);
        public override System.Drawing.Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(33, 39, 46);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(33, 39, 46);
        public override System.Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(38, 45, 53);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(20, 30, 38);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

