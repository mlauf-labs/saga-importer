using System.IO;
using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// Orchestrates importing a single file: stability gate -> upload -> (optionally) poll
/// until ready -> delete on success or move to the failed subfolder on error.
/// Never throws for an individual file; failures are logged and the file is moved aside.
/// </summary>
public sealed class ImportPipeline
{
    private readonly SagaClient _client;
    private readonly LogService _log;

    public ImportPipeline(SagaClient client, LogService log)
    {
        _client = client;
        _log = log;
    }

    public async Task<ImportResult> ProcessAsync(
        string path,
        AppSettings settings,
        string token,
        CancellationToken ct)
    {
        string fileName = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            return new ImportResult(fileName, ImportOutcome.Skipped, Message: "file no longer present");
        }

        if (!FileFilter.ShouldImport(fileName, settings.IncludeExtensions, settings.ExcludeExtensions))
        {
            _log.Debug($"Skipping filtered file: {fileName}");
            return new ImportResult(fileName, ImportOutcome.Skipped, Message: "filtered out");
        }

        bool stable = await FileStability.WaitUntilStableAsync(
            path,
            quietPeriod: settings.StabilityQuietPeriod,
            timeout: TimeSpan.FromMinutes(5),
            ct).ConfigureAwait(false);

        if (!stable)
        {
            // Still being written or vanished; a later rescan will pick it up again.
            _log.Debug($"File not stable yet, will retry later: {fileName}");
            return new ImportResult(fileName, ImportOutcome.Skipped, Message: "not stable yet");
        }

        _client.Configure(settings.BaseUrl, token);

        UploadResult upload;
        try
        {
            _log.Info($"Uploading: {fileName}");
            upload = await _client.UploadAsync(path, fileName, ct).ConfigureAwait(false);
        }
        catch (SagaException ex)
        {
            _log.Error($"Upload failed for {fileName}: {ex.Message}");
            return MoveToFailed(path, settings, $"upload failed: {ex.Message}");
        }

        _log.Info($"Uploaded {fileName} -> document {upload.DocumentId} ({upload.Status}).");

        if (settings.DeleteTrigger == DeleteTrigger.OnAccepted)
        {
            return DeleteSource(path, fileName, upload.DocumentId);
        }

        // If Saga already reports a terminal state at upload time, act on it now
        // instead of entering the polling loop.
        if (upload.Status.IsTerminal)
        {
            return upload.Status == DocumentStatus.Ready
                ? DeleteSource(path, fileName, upload.DocumentId)
                : MoveToFailed(path, settings, "ingestion failed at upload");
        }

        return await WaitForReadyThenFinalizeAsync(path, fileName, upload.DocumentId, settings, ct)
            .ConfigureAwait(false);
    }

    private async Task<ImportResult> WaitForReadyThenFinalizeAsync(
        string path,
        string fileName,
        string documentId,
        AppSettings settings,
        CancellationToken ct)
    {
        TimeSpan interval = settings.PollInterval;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + settings.ReadyTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            StatusResult status;
            try
            {
                status = await _client.GetStatusAsync(documentId, ct).ConfigureAwait(false);
            }
            catch (SagaException ex)
            {
                _log.Error($"Status check failed for {fileName} ({documentId}): {ex.Message}");
                return MoveToFailed(path, settings, $"status check failed: {ex.Message}");
            }

            switch (status.Status)
            {
                case DocumentStatus.Ready:
                    return DeleteSource(path, fileName, documentId);

                case DocumentStatus.Failed:
                    _log.Error($"Saga reported ingestion failed for {fileName}: {status.Error}");
                    return MoveToFailed(path, settings, $"ingestion failed: {status.Error}");

                default:
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    break;
            }
        }

        _log.Error($"Timed out waiting for {fileName} ({documentId}) to become ready.");
        return MoveToFailed(path, settings, "timed out waiting for ready status");
    }

    private ImportResult DeleteSource(string path, string fileName, string documentId)
    {
        try
        {
            File.Delete(path);
            _log.Info($"Imported and removed: {fileName} (document {documentId}).");
            return new ImportResult(fileName, ImportOutcome.Imported, documentId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Imported {fileName} but could not delete the source: {ex.Message}");
            return new ImportResult(
                fileName, ImportOutcome.Imported, documentId, "imported; source could not be deleted");
        }
    }

    private ImportResult MoveToFailed(string path, AppSettings settings, string reason)
    {
        string fileName = Path.GetFileName(path);
        try
        {
            string? parent = Path.GetDirectoryName(path);
            if (parent is null)
            {
                return new ImportResult(fileName, ImportOutcome.Failed, Message: reason);
            }

            string failedDir = Path.Combine(parent, settings.FailedSubfolderName);
            Directory.CreateDirectory(failedDir);

            string target = Path.Combine(failedDir, fileName);
            if (File.Exists(target))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                target = Path.Combine(
                    failedDir,
                    $"{Path.GetFileNameWithoutExtension(fileName)}.{stamp}{Path.GetExtension(fileName)}");
            }

            File.Move(path, target);
            _log.Warn($"Moved failed file to: {target} ({reason}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not move failed file {fileName}: {ex.Message}");
        }

        return new ImportResult(fileName, ImportOutcome.Failed, Message: reason);
    }
}
