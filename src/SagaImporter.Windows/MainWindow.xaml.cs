using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using SagaImporter.ViewModels;

namespace SagaImporter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // PasswordBox cannot be data-bound; sync it manually.
        TokenBox.Password = viewModel.Settings.ApiToken;
        TokenBox.PasswordChanged += (_, _) => _viewModel.Settings.ApiToken = TokenBox.Password;
        viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;

        _viewModel.Status.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the password box in sync when settings are reloaded.
        if (e.PropertyName == nameof(SettingsViewModel.ApiToken)
            && TokenBox.Password != _viewModel.Settings.ApiToken)
        {
            TokenBox.Password = _viewModel.Settings.ApiToken;
        }
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }

    /// <summary>Hide to tray instead of exiting; the tray menu provides Exit.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
