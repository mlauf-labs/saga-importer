using System.IO;
using SagaImporter.Services;
using Xunit;

namespace SagaImporter.Tests;

public class FileStabilityTests : IDisposable
{
    private readonly string _dir;

    public FileStabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dsi-stab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task WaitUntilStable_ReturnsTrue_ForStaticFile()
    {
        string path = Path.Combine(_dir, "static.bin");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        bool stable = await FileStability.WaitUntilStableAsync(
            path,
            quietPeriod: TimeSpan.FromMilliseconds(50),
            timeout: TimeSpan.FromSeconds(5),
            delay: (_, _) => Task.CompletedTask);

        Assert.True(stable);
    }

    [Fact]
    public async Task WaitUntilStable_ReturnsFalse_WhenFileMissing()
    {
        string path = Path.Combine(_dir, "missing.bin");

        bool stable = await FileStability.WaitUntilStableAsync(
            path,
            quietPeriod: TimeSpan.FromMilliseconds(50),
            timeout: TimeSpan.FromMilliseconds(200),
            delay: (_, _) => Task.CompletedTask);

        Assert.False(stable);
    }

    [Fact]
    public void CanOpenExclusively_ReturnsFalse_WhileFileIsOpenForWriting()
    {
        string path = Path.Combine(_dir, "locked.bin");
        using FileStream _ = new(path, FileMode.Create, FileAccess.Write, FileShare.None);

        Assert.False(FileStability.CanOpenExclusively(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
