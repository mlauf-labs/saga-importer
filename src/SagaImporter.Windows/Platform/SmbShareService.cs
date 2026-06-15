using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using SagaImporter.Services;

namespace SagaImporter.Windows.Platform;

public sealed record SmbShareResult(bool Success, string Message);

/// <summary>
/// Creates an SMB share for the watched folder. Because sharing requires administrator
/// rights and the app runs unprivileged, this launches a one-off elevated PowerShell
/// process (UAC prompt) only when invoked. Output is exchanged via a temp file since an
/// elevated process cannot have its stdout redirected.
/// </summary>
public sealed class SmbShareService
{
    private readonly LogService _log;

    public SmbShareService(LogService log) => _log = log;

    public static string SanitizeShareName(string raw)
    {
        var sb = new StringBuilder();
        foreach (char c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or ' ')
            {
                sb.Append(c);
            }
        }

        string name = sb.ToString().Trim();
        return name.Length == 0 ? "Inbox" : name;
    }

    public async Task<SmbShareResult> ShareFolderAsync(string folderPath, string shareName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return new SmbShareResult(false, "The watched folder does not exist.");
        }

        string name = SanitizeShareName(shareName);
        string account = $"{Environment.UserDomainName}\\{Environment.UserName}";
        string resultFile = Path.Combine(Path.GetTempPath(), $"dsi-share-{Guid.NewGuid():N}.txt");
        string scriptFile = Path.Combine(Path.GetTempPath(), $"dsi-share-{Guid.NewGuid():N}.ps1");

        string script = BuildScript(name, folderPath, account, resultFile);
        await File.WriteAllTextAsync(scriptFile, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            .ConfigureAwait(false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                UseShellExecute = true, // required for the runas verb (UAC)
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            _log.Info($"Requesting elevation to create SMB share '{name}'.");
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return new SmbShareResult(false, "Could not start the elevated helper.");
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            string message = File.Exists(resultFile)
                ? (await File.ReadAllTextAsync(resultFile).ConfigureAwait(false)).Trim()
                : "No result returned from the elevated helper.";

            bool success = process.ExitCode == 0;
            if (success)
            {
                _log.Info($"SMB share created: {message}");
                return new SmbShareResult(true, message.Length == 0 ? $"Share '{name}' created." : message);
            }

            _log.Warn($"SMB share failed: {message}");
            return new SmbShareResult(false, message);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user dismissed the UAC prompt.
            return new SmbShareResult(false, "Elevation was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error($"SMB share error: {ex.Message}");
            return new SmbShareResult(false, ex.Message);
        }
        finally
        {
            TryDelete(scriptFile);
            TryDelete(resultFile);
        }
    }

    private static string BuildScript(string name, string path, string account, string resultFile)
    {
        // Recreate the share if it already exists so the button is idempotent.
        return $$"""
            $ErrorActionPreference = 'Stop'
            $name = '{{Escape(name)}}'
            $path = '{{Escape(path)}}'
            $account = '{{Escape(account)}}'
            $out = '{{Escape(resultFile)}}'
            try {
                $existing = Get-SmbShare -Name $name -ErrorAction SilentlyContinue
                if ($existing) { Remove-SmbShare -Name $name -Force }
                New-SmbShare -Name $name -Path $path -FullAccess $account -Description 'Saga Importer inbox' | Out-Null
                "Share '$name' now points to '$path' (full access: $account)." | Out-File -FilePath $out -Encoding UTF8
                exit 0
            } catch {
                "Failed to create share: $($_.Exception.Message)" | Out-File -FilePath $out -Encoding UTF8
                exit 1
            }
            """;
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
