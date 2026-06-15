using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SagaImporter.Models;
using SagaImporter.Services;
using SagaImporter.Windows.Platform;

namespace SagaImporter.ViewModels;

/// <summary>Backs the Settings tab. Edits a working copy and applies it via the engine.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ImporterEngine _engine;
    private readonly SmbShareService _smb;

    [ObservableProperty]
    private string _watchedFolder = string.Empty;

    [ObservableProperty]
    private string _baseUrl = "http://localhost:8000";

    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private bool _deleteAfterReady = true;

    [ObservableProperty]
    private int _statusPollTimeoutMinutes = 10;

    [ObservableProperty]
    private int _rescanIntervalMinutes = 10;

    [ObservableProperty]
    private string _failedSubfolderName = "failed";

    [ObservableProperty]
    private string _includeExtensions = string.Empty;

    [ObservableProperty]
    private string _excludeExtensions = ".tmp,.crdownload,.part";

    [ObservableProperty]
    private int _stabilityDelaySeconds = 2;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _smbShareName = "Inbox";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(ImporterEngine engine, SmbShareService smb)
    {
        _engine = engine;
        _smb = smb;
        LoadFromEngine();
    }

    public void LoadFromEngine()
    {
        AppSettings s = _engine.CurrentSettings;
        WatchedFolder = s.WatchedFolder;
        BaseUrl = s.BaseUrl;
        ApiToken = _engine.CurrentToken;
        DeleteAfterReady = s.DeleteTrigger == DeleteTrigger.OnReady;
        StatusPollTimeoutMinutes = s.StatusPollTimeoutMinutes;
        RescanIntervalMinutes = s.RescanIntervalMinutes;
        FailedSubfolderName = s.FailedSubfolderName;
        IncludeExtensions = s.IncludeExtensions;
        ExcludeExtensions = s.ExcludeExtensions;
        StabilityDelaySeconds = s.StabilityDelaySeconds;
        StartWithWindows = s.StartWithWindows;
        SmbShareName = s.SmbShareName;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the folder to watch",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (!string.IsNullOrWhiteSpace(WatchedFolder))
        {
            dialog.SelectedPath = WatchedFolder;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            WatchedFolder = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void Save()
    {
        AppSettings s = new()
        {
            WatchedFolder = WatchedFolder?.Trim() ?? string.Empty,
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "http://localhost:8000" : BaseUrl.Trim(),
            DeleteTrigger = DeleteAfterReady ? DeleteTrigger.OnReady : DeleteTrigger.OnAccepted,
            StatusPollTimeoutMinutes = Math.Max(1, StatusPollTimeoutMinutes),
            RescanIntervalMinutes = Math.Max(1, RescanIntervalMinutes),
            FailedSubfolderName = string.IsNullOrWhiteSpace(FailedSubfolderName) ? "failed" : FailedSubfolderName.Trim(),
            IncludeExtensions = IncludeExtensions?.Trim() ?? string.Empty,
            ExcludeExtensions = ExcludeExtensions?.Trim() ?? string.Empty,
            StabilityDelaySeconds = Math.Max(0, StabilityDelaySeconds),
            StartWithWindows = StartWithWindows,
            SmbShareName = string.IsNullOrWhiteSpace(SmbShareName) ? "Inbox" : SmbShareName.Trim(),
        };

        _engine.ApplySettings(s, ApiToken?.Trim() ?? string.Empty);
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        StatusMessage = "Testing connection...";
        bool ok = await _engine.TestConnectionAsync(
            string.IsNullOrWhiteSpace(BaseUrl) ? "http://localhost:8000" : BaseUrl.Trim(),
            ApiToken?.Trim() ?? string.Empty);
        StatusMessage = ok ? "Connection OK." : "Could not reach Saga (check URL).";
    }

    [RelayCommand]
    private async Task ShareFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(WatchedFolder))
        {
            StatusMessage = "Set a watched folder before sharing.";
            return;
        }

        StatusMessage = "Requesting elevation to create the SMB share...";
        SmbShareResult result = await _smb.ShareFolderAsync(WatchedFolder.Trim(), SmbShareName);
        StatusMessage = result.Message;

        if (result.Success)
        {
            MessageBox.Show(
                result.Message,
                "SMB share created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
