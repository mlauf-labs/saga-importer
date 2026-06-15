using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SagaImporter.Models;
using SagaImporter.Services;

namespace SagaImporter.ViewModels;

/// <summary>Backs the Status/Log tab: live log, counters, connection state and controls.</summary>
public sealed partial class StatusViewModel : ObservableObject
{
    private const int MaxLogLines = 1000;

    private readonly ImporterEngine _engine;

    [ObservableProperty]
    private string _connectionState = "Not tested";

    [ObservableProperty]
    private int _queueCount;

    [ObservableProperty]
    private int _importedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeText))]
    private bool _isPaused;

    [ObservableProperty]
    private string _watchedFolder = string.Empty;

    public ObservableCollection<string> LogLines { get; } = new();

    public string PauseResumeText => IsPaused ? "Resume" : "Pause";

    public StatusViewModel(ImporterEngine engine)
    {
        _engine = engine;

        foreach (LogEntry entry in _engine.Log.Snapshot())
        {
            LogLines.Add(entry.Display);
        }

        _engine.Log.EntryAdded += OnLogEntry;
        _engine.Queue.StateChanged += OnQueueStateChanged;

        RefreshFromEngine();
    }

    private void OnLogEntry(LogEntry entry) =>
        OnUi(() =>
        {
            LogLines.Add(entry.Display);
            while (LogLines.Count > MaxLogLines)
            {
                LogLines.RemoveAt(0);
            }
        });

    private void OnQueueStateChanged() => OnUi(RefreshFromEngine);

    public void RefreshFromEngine()
    {
        QueueCount = _engine.Queue.QueueCount;
        ImportedCount = _engine.Queue.Imported;
        FailedCount = _engine.Queue.Failed;
        SkippedCount = _engine.Queue.Skipped;
        IsPaused = _engine.Queue.IsPaused;
        WatchedFolder = _engine.CurrentSettings.WatchedFolder;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        AppSettings s = _engine.CurrentSettings;
        ConnectionState = "Testing...";
        bool ok = await _engine.TestConnectionAsync(s.BaseUrl, _engine.CurrentToken);
        ConnectionState = ok ? "Connected" : "Unreachable";
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_engine.Queue.IsPaused)
        {
            _engine.Queue.Resume();
        }
        else
        {
            _engine.Queue.Pause();
        }
    }

    [RelayCommand]
    private void OpenWatchedFolder()
    {
        string folder = _engine.CurrentSettings.WatchedFolder;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void RescanNow() => _engine.Watcher.Rescan();

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    private static void OnUi(Action action)
    {
        Application? app = Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
