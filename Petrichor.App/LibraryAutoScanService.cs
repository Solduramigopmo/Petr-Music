using System.IO;
using System.Threading;

namespace Petrichor.App;

internal sealed class LibraryAutoScanService : IDisposable
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".alac"
    ];

    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan debounceInterval;
    private readonly object sync = new();
    private System.Threading.Timer? debounceTimer;
    private bool disposed;

    public LibraryAutoScanService(TimeSpan? debounceInterval = null)
    {
        this.debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(850);
    }

    public event EventHandler? ChangeDetected;

    public bool Enabled { get; private set; } = true;

    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            Enabled = enabled;
        }
    }

    public void UpdateFolders(IEnumerable<string> folderPaths)
    {
        lock (sync)
        {
            EnsureNotDisposed();

            var normalizedPaths = folderPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var expectedPaths = new HashSet<string>(normalizedPaths, StringComparer.OrdinalIgnoreCase);

            foreach (var existing in watchers.Keys.Where(path => !expectedPaths.Contains(path)).ToArray())
            {
                watchers[existing].Dispose();
                watchers.Remove(existing);
            }

            foreach (var folderPath in normalizedPaths)
            {
                if (watchers.ContainsKey(folderPath) || !Directory.Exists(folderPath))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.Error += OnWatcherError;

                watchers[folderPath] = watcher;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            debounceTimer?.Dispose();
            debounceTimer = null;

            foreach (var watcher in watchers.Values)
            {
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        ScheduleChangeNotification();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!Enabled)
        {
            return;
        }

        if (!IsSupportedFileEvent(e.FullPath))
        {
            return;
        }

        ScheduleChangeNotification();
    }

    private void ScheduleChangeNotification()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            debounceTimer?.Dispose();
            debounceTimer = new System.Threading.Timer(
                static state =>
                {
                    if (state is LibraryAutoScanService service)
                    {
                        service.ChangeDetected?.Invoke(service, EventArgs.Empty);
                    }
                },
                this,
                dueTime: debounceInterval,
                period: Timeout.InfiniteTimeSpan);
        }
    }

    private static bool IsSupportedFileEvent(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        var extension = Path.GetExtension(fullPath);
        return string.IsNullOrWhiteSpace(extension) || SupportedExtensions.Contains(extension);
    }

    private void EnsureNotDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LibraryAutoScanService));
        }
    }
}
