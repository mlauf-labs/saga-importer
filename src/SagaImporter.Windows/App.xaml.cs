using System.Threading;
using System.Windows;
using SagaImporter.Models;
using SagaImporter.Services;
using SagaImporter.ViewModels;
using SagaImporter.Windows.Platform;
using Hardcodet.Wpf.TaskbarNotification;

namespace SagaImporter;

public partial class App : Application
{
    private const string MutexName = "SagaImporter.SingleInstance.{8E2A0C1F-7C3D-4F2A-9B11-3B6F2E5A77D2}";

    private Mutex? _singleInstanceMutex;
    private ImporterEngine? _engine;
    private SmbShareService? _smb;
    private TrayIconService? _tray;
    private MainViewModel? _viewModel;
    private MainWindow? _window;

    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running.
            MessageBox.Show(
                "Saga Importer is already running (check the system tray).",
                "Saga Importer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _engine = new ImporterEngine(
            new DpapiTokenProtector(),
            new RegistryAutostartService());

        _smb = new SmbShareService(_engine.Log);
        _viewModel = new MainViewModel(_engine, _smb);

        _tray = new TrayIconService();
        _tray.OpenRequested += ShowMainWindow;
        _tray.TogglePauseRequested += TogglePause;
        _tray.ExitRequested += ExitApplication;

        _engine.Queue.StateChanged += () =>
            Dispatcher.Invoke(() => _tray?.SetPaused(_engine!.Queue.IsPaused));
        _engine.Queue.ItemCompleted += OnItemCompleted;

        _engine.Start();

        if (!_engine.CurrentSettings.HasRequiredFields)
        {
            // First run / incomplete config: open the window so the user can set it up.
            ShowMainWindow();
            _tray.ShowBalloon(
                "Saga Importer",
                "Configure the watched folder, Saga URL and API token to begin.");
        }
    }

    private void OnItemCompleted(ImportResult result)
    {
        if (result.Outcome == ImportOutcome.Failed)
        {
            Dispatcher.Invoke(() =>
                _tray?.ShowBalloon(
                    "Import failed",
                    $"{result.FileName}: {result.Message}",
                    BalloonIcon.Warning));
        }
    }

    private void ShowMainWindow()
    {
        _window ??= new MainWindow(_viewModel!);

        // Refresh the settings form in case files changed it on disk.
        _viewModel!.Settings.LoadFromEngine();
        _viewModel.Status.RefreshFromEngine();

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void TogglePause()
    {
        if (_engine is null)
        {
            return;
        }

        if (_engine.Queue.IsPaused)
        {
            _engine.Queue.Resume();
        }
        else
        {
            _engine.Queue.Pause();
        }
    }

    private void ExitApplication()
    {
        IsShuttingDown = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _tray?.Dispose();
        _engine?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (e.g. duplicate-instance path); ignore.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
