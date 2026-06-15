using System.IO;

namespace SagaImporter.Services;

/// <summary>Decides whether a file should be imported based on extension allow/block lists.</summary>
public static class FileFilter
{
    public static bool ShouldImport(string fileName, string includeExtensions, string excludeExtensions)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        HashSet<string> exclude = Parse(excludeExtensions);
        if (exclude.Contains(ext))
        {
            return false;
        }

        HashSet<string> include = Parse(includeExtensions);
        if (include.Count > 0 && !include.Contains(ext))
        {
            return false;
        }

        return true;
    }

    private static HashSet<string> Parse(string csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return set;
        }

        foreach (string raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string ext = raw.StartsWith('.') ? raw : "." + raw;
            set.Add(ext.ToLowerInvariant());
        }

        return set;
    }
}
