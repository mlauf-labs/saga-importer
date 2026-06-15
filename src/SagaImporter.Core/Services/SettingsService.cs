using System.IO;
using System.Text.Json;
using SagaImporter.Abstractions;
using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. The default path is
/// <c>%APPDATA%\SagaImporter\settings.json</c> on Windows or the value supplied
/// by the host (e.g. <c>/etc/saga-importer/settings.json</c> on Linux).
/// Token protection is delegated to an injected <see cref="ITokenProtector"/>;
/// defaults to <see cref="IdentityTokenProtector"/> when none is provided.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _settingsPath;
    private readonly ITokenProtector _protector;

    public SettingsService(string? settingsPath = null, ITokenProtector? protector = null)
    {
        if (settingsPath is not null)
        {
            _settingsPath = settingsPath;
        }
        else
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SagaImporter");
            _settingsPath = Path.Combine(dir, "settings.json");
        }

        _protector = protector ?? IdentityTokenProtector.Instance;
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to defaults rather than crash on a corrupt settings file.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        // Write atomically: write to a temp file then replace.
        string tmp = _settingsPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_settingsPath))
        {
            File.Replace(tmp, _settingsPath, null);
        }
        else
        {
            File.Move(tmp, _settingsPath);
        }
    }

    /// <summary>Returns the decrypted API token from the settings object.</summary>
    public string GetApiToken(AppSettings settings) =>
        _protector.Unprotect(settings.EncryptedApiToken);

    /// <summary>Encrypts the plaintext token and stores it in the settings object.</summary>
    public void SetApiToken(AppSettings settings, string plaintextToken) =>
        settings.EncryptedApiToken = _protector.Protect(plaintextToken);
}
