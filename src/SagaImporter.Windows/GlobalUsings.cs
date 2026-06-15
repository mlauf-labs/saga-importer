// WinForms is enabled only for FolderBrowserDialog. Disambiguate the few types that
// exist in both WPF and WinForms toward their WPF versions for the rest of the app.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using MenuItem = System.Windows.Controls.MenuItem;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using Separator = System.Windows.Controls.Separator;
