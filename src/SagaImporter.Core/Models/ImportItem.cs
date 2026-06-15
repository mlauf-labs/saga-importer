using System.IO;

namespace SagaImporter.Models;

/// <summary>
/// A file queued for import. Equality is based on the full path so the queue can
/// de-duplicate the same file arriving from both an event and a rescan.
/// </summary>
public sealed class ImportItem : IEquatable<ImportItem>
{
    public ImportItem(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
    }

    public string FullPath { get; }

    public string FileName { get; }

    public bool Equals(ImportItem? other) =>
        other is not null
        && string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ImportItem);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(FullPath);

    public override string ToString() => FullPath;
}
