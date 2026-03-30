using System.Collections.Generic;

namespace Petrichor.Core.Migration;

public static class ParityCatalog
{
    public static IReadOnlyList<ParityItem> Items { get; } =
    [
        new(
            "App shell",
            "Main window, split layout, toolbar navigation",
            "PetrichorApp.swift + Views/Main/ContentView.swift",
            "Rebuild in WPF shell first, keep room for later UI layer swap if needed",
            ParityStatus.InProgress),
        new(
            "Library",
            "Folder mapping and refresh",
            "Managers/Library/*",
            "Windows folder picker, persisted folder registry, scan services",
            ParityStatus.Planned),
        new(
            "Playback",
            "Queue, repeat, shuffle, progress, state restore",
            "Managers/PlaybackManager.swift + Managers/Playlist/*",
            "Port semantics into Petrichor.Core and bind in Petrichor.Media",
            ParityStatus.InProgress),
        new(
            "Platform",
            "Menubar, dock and updater",
            "Application/AppDelegate.swift + Managers/MenuBarManager.swift",
            "Tray icon, shell integrations and Windows updater replacement",
            ParityStatus.AdaptationRequired)
    ];
}
