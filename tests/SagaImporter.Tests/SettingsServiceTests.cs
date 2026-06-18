using System.IO;
using SagaImporter.Abstractions;
using SagaImporter.Models;
using SagaImporter.Services;
using SagaImporter.Windows.Platform;
using Xunit;

namespace SagaImporter.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dsi-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    public void Load_WhenMissing_ReturnsDefaults()
    {
        var service = new SettingsService(_path);
        AppSettings settings = service.Load();

        Assert.Equal("http://localhost:8000", settings.BaseUrl);
        Assert.Equal(DeleteTrigger.OnReady, settings.DeleteTrigger);
        Assert.Equal(10, settings.RescanIntervalMinutes);
        Assert.Equal(4, settings.MaxConcurrentImports);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var service = new SettingsService(_path);
        var original = new AppSettings
        {
            WatchedFolder = @"C:\Inbox",
            BaseUrl = "http://example:9000",
            DeleteTrigger = DeleteTrigger.OnAccepted,
            RescanIntervalMinutes = 5,
            MaxConcurrentImports = 8,
            ExcludeExtensions = ".tmp",
        };

        service.Save(original);
        AppSettings loaded = service.Load();

        Assert.Equal(@"C:\Inbox", loaded.WatchedFolder);
        Assert.Equal("http://example:9000", loaded.BaseUrl);
        Assert.Equal(DeleteTrigger.OnAccepted, loaded.DeleteTrigger);
        Assert.Equal(5, loaded.RescanIntervalMinutes);
        Assert.Equal(8, loaded.MaxConcurrentImports);
        Assert.Equal(".tmp", loaded.ExcludeExtensions);
    }

    [Fact]
    public void ApiToken_FakeProtector_IsTransformedAtRestAndRoundTrips()
    {
        var service = new SettingsService(_path, new FakeTokenProtector());
        var settings = new AppSettings();

        service.SetApiToken(settings, "super-secret-token");
        service.Save(settings);

        // The fake protector prefixes tokens so plaintext is not literally present.
        string raw = File.ReadAllText(_path);
        Assert.DoesNotContain("super-secret-token", raw);

        AppSettings loaded = service.Load();
        Assert.Equal("super-secret-token", service.GetApiToken(loaded));
    }

    [Fact]
    public void ApiToken_Dpapi_IsEncryptedAtRestAndRoundTrips()
    {
        if (!OperatingSystem.IsWindows())
        {
            // DPAPI is Windows-only; skip on other platforms.
            return;
        }

        var service = new SettingsService(_path, new DpapiTokenProtector());
        var settings = new AppSettings();

        service.SetApiToken(settings, "super-secret-token");
        service.Save(settings);

        string raw = File.ReadAllText(_path);
        Assert.DoesNotContain("super-secret-token", raw);

        AppSettings loaded = service.Load();
        Assert.Equal("super-secret-token", service.GetApiToken(loaded));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

/// <summary>
/// Test-only token protector: Base64-encodes the token so the value at rest is never
/// the plaintext, without depending on any platform-specific encryption.
/// </summary>
file sealed class FakeTokenProtector : ITokenProtector
{
    public string Protect(string plaintext) =>
        string.IsNullOrEmpty(plaintext)
            ? string.Empty
            : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
        catch (FormatException)
        {
            return protectedValue;
        }
    }
}
