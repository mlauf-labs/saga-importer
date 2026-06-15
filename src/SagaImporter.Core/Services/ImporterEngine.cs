using System.Threading;
using SagaImporter.Abstractions;
using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// Top-level coordinator that owns the long-lived services and wires the watcher to the
/// import queue. View-models and host workers talk to this rather than the individual
/// services. Holds the current settings and decrypted token, and applies settings
/// changes consistently.
/// </summary>
public sealed class ImporterEngine : IDisposable
{
    private readonly Lock _settingsGate = new();
    private readonly IAutostartService _autostart;

    public ImporterEngine(
        ITokenProtector tokenProtector,
        IAutostartService autostartService,
        string? settingsPath = null,
        string? logDirectory = null)
    {
        _autostart = autostartService;

        Log = new LogService(logDirectory);
        Settings = new SettingsService(settingsPath, tokenProtector);
        Client = new SagaClient();
        Pipeline = new ImportPipeline(Client, Log);
        Watcher = new FolderWatcher(Log);
        Queue = new ImportQueue(Pipeline, Log, GetConfig);

        _current = Settings.Load();
        _token = Settings.GetApiToken(_current);

        Watcher.FileDetected += Queue.Enqueue;
    }

    private AppSettings _current;
    private string _token;

    public SettingsService Settings { get; }

    public LogService Log { get; }

    public SagaClient Client { get; }

    public ImportPipeline Pipeline { get; }

    public FolderWatcher Watcher { get; }

    public ImportQueue Queue { get; }

    /// <summary>Returns a defensive copy of the current settings.</summary>
    public AppSettings CurrentSettings
    {
        get
        {
            lock (_settingsGate)
            {
                return _current.Clone();
            }
        }
    }

    public string CurrentToken
    {
        get
        {
            lock (_settingsGate)
            {
                return _token;
            }
        }
    }

    /// <summary>Starts the queue worker and begins watching using the loaded settings.</summary>
    public void Start()
    {
        Log.Info("Saga Importer started.");
        Queue.Start();
        Client.Configure(_current.BaseUrl, _token);
        StartWatching();
    }

    /// <summary>
    /// Persists new settings and re-applies them: reconfigures the client, restarts the
    /// watcher, and updates the autostart registration. The plaintext token is encrypted
    /// before saving.
    /// </summary>
    public void ApplySettings(AppSettings updated, string plaintextToken)
    {
        Settings.SetApiToken(updated, plaintextToken);

        lock (_settingsGate)
        {
            _current = updated;
            _token = plaintextToken;
        }

        Settings.Save(updated);
        Client.Configure(updated.BaseUrl, plaintextToken);

        try
        {
            _autostart.Apply(updated.StartWithWindows);
            if (updated.StartWithWindows)
            {
                Log.Info("Autostart enabled.");
            }
            else
            {
                Log.Debug("Autostart disabled or not supported on this platform.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not update autostart setting: {ex.Message}");
        }

        StartWatching();
        Log.Info("Settings saved and applied.");
    }

    public Task<bool> TestConnectionAsync(string baseUrl, string token, CancellationToken ct = default)
    {
        var probe = new SagaClient();
        probe.Configure(baseUrl, token);
        return probe.CheckHealthAsync(ct);
    }

    private void StartWatching()
    {
        AppSettings s = CurrentSettings;
        if (string.IsNullOrWhiteSpace(s.WatchedFolder))
        {
            Watcher.Stop();
            Log.Warn("No watched folder configured. Set one in Settings to begin importing.");
            return;
        }

        Watcher.Start(s.WatchedFolder, s.FailedSubfolderName, s.RescanInterval);
    }

    private (AppSettings Settings, string Token) GetConfig()
    {
        lock (_settingsGate)
        {
            return (_current.Clone(), _token);
        }
    }

    public void Dispose()
    {
        Watcher.FileDetected -= Queue.Enqueue;
        Watcher.Dispose();
        Queue.Dispose();
        Log.Dispose();
    }
}
