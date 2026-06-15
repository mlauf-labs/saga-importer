using SagaImporter.Daemon.Platform;
using SagaImporter.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SagaImporter.Daemon;

/// <summary>
/// Hosted background service that owns the <see cref="ImporterEngine"/> lifetime.
/// Starts the engine on host startup and disposes it on graceful shutdown, forwarding
/// systemd's SIGTERM via the <see cref="CancellationToken"/>.
/// </summary>
public sealed class ImporterWorker : BackgroundService
{
    private readonly ILogger<ImporterWorker> _logger;
    private ImporterEngine? _engine;

    public ImporterWorker(ILogger<ImporterWorker> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string settingsPath = Environment.GetEnvironmentVariable("SAGA_SETTINGS_PATH")
            ?? "/etc/saga-importer/settings.json";

        string logDir = Environment.GetEnvironmentVariable("SAGA_LOG_DIR")
            ?? "/var/log/saga-importer";

        _engine = new ImporterEngine(
            new FilePermissionTokenProtector(),
            new NoopAutostartService(),
            settingsPath,
            logDir);

        _engine.Start();

        string folder = _engine.CurrentSettings.WatchedFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning(
                "No watched folder configured. Edit {SettingsPath} and restart the service.",
                settingsPath);
        }
        else
        {
            _logger.LogInformation("Saga Importer daemon watching: {Folder}", folder);
        }

        // The engine drives itself via its internal queue and watcher.
        // Keep this task alive until the host requests cancellation (SIGTERM / systemd stop).
        return Task.Delay(Timeout.Infinite, stoppingToken)
            .ContinueWith(
                static t => { /* swallow OperationCanceledException on graceful shutdown */ },
                TaskContinuationOptions.OnlyOnCanceled);
    }

    public override void Dispose()
    {
        _engine?.Dispose();
        base.Dispose();
    }
}
