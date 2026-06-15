using SagaImporter.Services;
using SagaImporter.Windows.Platform;

namespace SagaImporter.ViewModels;

/// <summary>Root view-model for the main window; exposes the tab view-models.</summary>
public sealed class MainViewModel
{
    public MainViewModel(ImporterEngine engine, SmbShareService smb)
    {
        Status = new StatusViewModel(engine);
        Settings = new SettingsViewModel(engine, smb);
    }

    public StatusViewModel Status { get; }

    public SettingsViewModel Settings { get; }
}
