using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace SagaImporter.Services;

/// <summary>
/// Owns the system-tray icon and its context menu. Created programmatically so the app
/// can run window-less in the tray. Raises actions the host wires to the window/engine.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _pauseItem;

    public TrayIconService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "Saga Importer",
            Visibility = Visibility.Visible,
        };

        TrySetIcon();

        _pauseItem = new MenuItem { Header = "Pause" };
        _pauseItem.Click += (_, _) => TogglePauseRequested?.Invoke();

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, _) => OpenRequested?.Invoke();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        _icon.ContextMenu = menu;

        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? TogglePauseRequested;

    public event Action? ExitRequested;

    public void SetPaused(bool paused) =>
        _pauseItem.Header = paused ? "Resume" : "Pause";

    public void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info) =>
        _icon.ShowBalloonTip(title, message, icon);

    private void TrySetIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute);
            System.Windows.Resources.StreamResourceInfo? info = Application.GetResourceStream(uri);
            if (info?.Stream is { } stream)
            {
                _icon.Icon = new System.Drawing.Icon(stream);
            }
        }
        catch (Exception)
        {
            // Fall back to no custom icon rather than crashing the tray.
        }
    }

    public void Dispose() => _icon.Dispose();
}
