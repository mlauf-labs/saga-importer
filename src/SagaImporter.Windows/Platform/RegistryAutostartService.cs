using Microsoft.Win32;
using SagaImporter.Abstractions;

namespace SagaImporter.Windows.Platform;

/// <summary>
/// Toggles "start with Windows" by writing the current executable path to the
/// per-user <c>Run</c> registry key. No admin rights required (HKCU).
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SagaImporter";

    private readonly string _executablePath;

    public RegistryAutostartService(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
    }

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            if (string.IsNullOrEmpty(_executablePath))
            {
                throw new InvalidOperationException("Cannot enable autostart: executable path is unknown.");
            }

            key.SetValue(ValueName, $"\"{_executablePath}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
