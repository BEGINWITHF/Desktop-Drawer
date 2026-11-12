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
    /// When true, folders (which have no suffix) can join this group.
    /// </summary>
    public bool IncludeFolders { get; set; }

    /// <summary>
    /// Lower-cased name fragments. A folder joins only when its name contains
    /// one of them; an empty list means every folder is included.
    /// </summary>
    public List<string> FolderNames { get; set; } = [];

    /// <summary>
    /// Paths the user explicitly removed from this group even though a suffix
    /// rule would otherwise match them.
    /// </summary>
    public List<string> Excluded { get; set; } = [];

    public bool MatchesExtension(string fileName) =>
        Extensions.Exists(extension =>
            fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public bool MatchesFolderName(string folderName) =>
        FolderNames.Count == 0 ||
        FolderNames.Exists(fragment =>
            folderName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public bool Matches(string name, bool isFolder) =>
        (IncludeFolders && isFolder && MatchesFolderName(name)) ||
        MatchesExtension(name);

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
            string value = token.Trim().ToLowerInvariant();

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

            string value = name.Trim().ToLowerInvariant();

            if (value.Length > 0 && !normalized.Contains(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }
}
