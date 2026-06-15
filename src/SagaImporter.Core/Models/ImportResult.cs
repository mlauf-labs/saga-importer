namespace SagaImporter.Models;

public enum ImportOutcome
{
    /// <summary>File uploaded and (if configured) confirmed ready; source deleted.</summary>
    Imported,

    /// <summary>Upload or ingestion failed; the file was moved to the failed subfolder.</summary>
    Failed,

    /// <summary>The file was skipped (filtered out or no longer present).</summary>
    Skipped,
}

/// <summary>Outcome of a single file import attempt.</summary>
public sealed record ImportResult(
    string FileName,
    ImportOutcome Outcome,
    string? DocumentId = null,
    string? Message = null);
