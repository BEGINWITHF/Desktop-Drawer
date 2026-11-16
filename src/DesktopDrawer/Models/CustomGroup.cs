using System.IO;

namespace DesktopDrawer.Models;

/// <summary>
/// A user-defined virtual category. Items are stored as lower-cased full paths
/// so they can be matched without moving or renaming anything on disk.
/// </summary>
public sealed class CustomGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = [];

    /// <summary>
    /// Normalised suffixes such as ".pdf" or ".tar.gz". A file whose name ends
    /// with one of them joins this group automatically.
    /// </summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>
    /// Lower-cased fragments matched against a file name without its extension.
    /// A file joins when one of them is contained in that stem.
    /// </summary>
    public List<string> FileNameFilters { get; set; } = [];

    /// <summary>
    /// When true, folders (which have no suffix) can join this group. The name
    /// fragments in <see cref="FolderNames"/> narrow that down; an empty list
    /// includes every folder.
    /// </summary>
    public bool IncludeFolders { get; set; }

    /// <summary>
    /// Lower-cased fragments matched against folder names.
    /// </summary>
    public List<string> FolderNames { get; set; } = [];

    /// <summary>
    /// Paths the user explicitly removed from this group even though a rule
    /// would otherwise match them.
    /// </summary>
    public List<string> Excluded { get; set; } = [];

    public bool MatchesExtension(string fileName) =>
        Extensions.Exists(extension =>
            fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public bool MatchesFileName(string fileName)
    {
        if (FileNameFilters.Count == 0)
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);

        return FileNameFilters.Exists(fragment =>
            stem.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public bool MatchesFolderName(string folderName) =>
        FolderNames.Count == 0 ||
        FolderNames.Exists(fragment =>
            folderName.Contains(fragment, StringComparison.Ordinal));

    public bool Matches(string name, bool isFolder)
    {
        if (isFolder)
        {
            return IncludeFolders && MatchesFolderName(name);
        }

        return MatchesExtension(name) || MatchesFileName(name);
    }

    public static string? NormalizeExtension(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string value = token.Trim().ToLowerInvariant().TrimStart('*');

        if (value.Length == 0)
        {
            return null;
        }

        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        return value == "." ? null : value;
    }

    public static List<string> ParseExtensions(string? text)
    {
        var extensions = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return extensions;
        }

        string[] tokens = text.Split(
            [',', ';', '|', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            string? extension = NormalizeExtension(token);

            if (extension is not null && !extensions.Contains(extension))
            {
                extensions.Add(extension);
            }
        }

        return extensions;
    }

    public static List<string> NormalizeExtensions(IEnumerable<string>? extensions)
    {
        var normalized = new List<string>();

        if (extensions is null)
        {
            return normalized;
        }

        foreach (string extension in extensions)
        {
            string? value = NormalizeExtension(extension);

            if (value is not null && !normalized.Contains(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    public static string FormatExtensions(IEnumerable<string>? extensions) =>
        extensions is null
            ? string.Empty
            : string.Join(", ", extensions.Select(extension => extension.TrimStart('.')));

    public static List<string> ParseFileNameFilters(string? text)
    {
        var filters = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return filters;
        }

        string[] tokens = text.Split(
            [',', ';', '|', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            string value = token.Trim().ToLowerInvariant();

            if (value.Length > 0 && !filters.Contains(value))
            {
                filters.Add(value);
            }
        }

        return filters;
    }

    public static List<string> NormalizeFileNameFilters(IEnumerable<string>? filters)
    {
        var normalized = new List<string>();

        if (filters is null)
        {
            return normalized;
        }

        foreach (string filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                continue;
            }

            string value = filter.Trim().ToLowerInvariant();

            if (value.Length > 0 && !normalized.Contains(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    public static List<string> ParseFolderNames(string? text)
    {
        var names = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return names;
        }

        string[] tokens = text.Split(
            [',', ';', '|', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            string value = token.Trim();

            if (value.Length > 0 && !names.Contains(value))
            {
                names.Add(value);
            }
        }

        return names;
    }

    public static List<string> NormalizeFolderNames(IEnumerable<string>? names)
    {
        var normalized = new List<string>();

        if (names is null)
        {
            return normalized;
        }

        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string value = name.Trim();

            if (value.Length > 0 && !normalized.Contains(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }
}
