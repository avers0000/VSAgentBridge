namespace VSAgentBridge.Logic;

/// <summary>
/// The folders where the bridge works (workspace roots) as the user enters them in init:
/// absolute paths separated by ';'. Pure string logic: whether the folders exist is checked by the caller.
/// </summary>
internal static class WorkspaceRoots
{
    public const char Separator = ';';

    /// <summary>
    /// Parses <paramref name="input"/>. Entries are trimmed and empty entries skipped; each path is
    /// normalized (full path, no trailing separator except for a drive root); duplicates are dropped
    /// case-insensitively, the first one is kept. Returns false with a message for the user if an entry
    /// is not an absolute path or no folder is left.
    /// </summary>
    public static bool TryParse(string? input, out IReadOnlyList<string> roots, out string? error)
    {
        var result = new List<string>();
        var invalid = new List<string>();

        foreach (var part in (input ?? "").Split(Separator))
        {
            var entry = part.Trim();
            if (entry.Length == 0)
                continue;

            var normalized = Normalize(entry);
            if (normalized == null)
                invalid.Add(entry);
            else if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                result.Add(normalized);
        }

        if (invalid.Count > 0)
        {
            roots = Array.Empty<string>();
            error = "Not an absolute folder path (for example D:\\Projects): "
                + string.Join(", ", invalid.Select(e => $"\"{e}\""));
            return false;
        }

        if (result.Count == 0)
        {
            roots = Array.Empty<string>();
            error = "Enter at least one folder: without workspace roots the bridge works nowhere.";
            return false;
        }

        roots = result;
        error = null;
        return true;
    }

    /// <summary>The list as one string, the way the plugin option "workspaceRoots" stores it.</summary>
    public static string Format(IEnumerable<string> roots) => string.Join(Separator.ToString(), roots);

    // Relative and drive-relative paths would depend on the current directory of whoever starts the bridge.
    // GetFullPath turns "/" into "\" and resolves "." and ".."; it throws on characters not valid in a path.
    private static string? Normalize(string entry)
    {
        if (!PathUtil.IsFullyQualified(entry))
            return null;

        try
        {
            var full = Path.GetFullPath(entry);
            var rootLength = Path.GetPathRoot(full)?.Length ?? 0;
            return full.Length > rootLength ? full.TrimEnd('\\', '/') : full;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return null;
        }
    }
}