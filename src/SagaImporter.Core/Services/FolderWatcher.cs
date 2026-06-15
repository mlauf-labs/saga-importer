using System.IO;
using System.Threading;

namespace SagaImporter.Services;

/// <summary>
/// Watches a folder for new files using <see cref="FileSystemWatcher"/> (event-driven)
/// plus a low-frequency safety rescan timer. Detected files are reported via
/// <see cref="FileDetected"/>; the consumer is responsible for stability checks,
/// filtering and importing. Files inside the failed subfolder are ignored.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly LogService _log;
    private readonly Lock _gate = new();

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _rescanTimer;
    private string _folder = string.Empty;
    private string _failedSubfolder = "failed";
    private bool _disposed;

    public FolderWatcher(LogService log) => _log = log;

    /// <summary>Raised with the full path of a file that may need importing.</summary>
    public event Action<string>? FileDetected;

    public bool IsRunning { get; private set; }

    /// <summary>(Re)starts watching the given folder and schedules periodic rescans.</summary>
    public void Start(string folder, string failedSubfolderName, TimeSpan rescanInterval)
    {
        lock (_gate)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                _log.Warn($"Watched folder does not exist: '{folder}'. Watching is paused.");
                return;
            }

            _folder = folder;
            _failedSubfolder = failedSubfolderName;

            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Changed += OnChanged;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;

            long ms = (long)Math.Max(TimeSpan.FromMinutes(1).TotalMilliseconds, rescanInterval.TotalMilliseconds);
            _rescanTimer = new System.Threading.Timer(_ => Rescan(), null, ms, ms);

            IsRunning = true;
            _log.Info($"Watching '{folder}' (rescan every {rescanInterval.TotalMinutes:0} min).");

            // Catch files already present at startup.
            Rescan();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _rescanTimer?.Dispose();
            _rescanTimer = null;

            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnChanged;
                _watcher.Renamed -= OnRenamed;
                _watcher.Changed -= OnChanged;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
            }

            IsRunning = false;
        }
    }

    /// <summary>Enumerates the watched folder and reports every candidate file.</summary>
    public void Rescan()
    {
        string folder = _folder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                Report(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Rescan failed: {ex.Message}");
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Report(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Report(e.FullPath);

    private void OnError(object sender, ErrorEventArgs e)
    {
        _log.Warn($"File watcher error: {e.GetException().Message}. Restarting watcher.");
        // A buffer overflow invalidates the watcher; restart and rescan to recover.
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }

            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Ignore; the next rescan will recover.
            }
        }

        Rescan();
    }

    private void Report(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        // Ignore anything inside the failed subfolder.
        string? parent = Path.GetDirectoryName(path);
        if (parent is not null
            && string.Equals(
                Path.GetFileName(parent),
                _failedSubfolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FileDetected?.Invoke(path);
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
