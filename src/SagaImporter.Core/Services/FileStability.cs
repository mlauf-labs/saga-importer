using System.IO;

namespace SagaImporter.Services;

/// <summary>
/// Helpers to decide when a file has finished being copied into the watched folder,
/// so we never upload a partially-written file.
/// </summary>
public static class FileStability
{
    /// <summary>
    /// Returns true if the file can be opened for exclusive read (i.e. no other process
    /// holds a write handle on it).
    /// </summary>
    public static bool CanOpenExclusively(string path)
    {
        try
        {
            using FileStream _ = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until the file's size stays constant for <paramref name="quietPeriod"/> and
    /// it can be opened exclusively, or until <paramref name="timeout"/> elapses.
    /// </summary>
    /// <returns>true if the file became stable; false on timeout or if it disappeared.</returns>
    public static async Task<bool> WaitUntilStableAsync(
        string path,
        TimeSpan quietPeriod,
        TimeSpan timeout,
        CancellationToken ct = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long lastSize = -1;
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(250, quietPeriod.TotalMilliseconds / 2));

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                return false;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                size = -1;
            }

            if (size >= 0 && size == lastSize && CanOpenExclusively(path))
            {
                // Size held steady across a poll interval and the file is not locked.
                await delay(quietPeriod, ct).ConfigureAwait(false);
                if (File.Exists(path)
                    && new FileInfo(path).Length == size
                    && CanOpenExclusively(path))
                {
                    return true;
                }
            }

            lastSize = size;
            await delay(pollInterval, ct).ConfigureAwait(false);
        }

        return false;
    }
}
