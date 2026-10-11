namespace VSAgentBridge.Logic;

/// <summary>Path helpers. Pure string logic: no file system access.</summary>
internal static class PathUtil
{
    /// <summary>
    /// Returns an absolute, normalized path. Relative paths are resolved against
    /// <paramref name="baseDirectory"/> (agents may send paths relative to their working directory).
    /// </summary>
    public static string ToFullPath(string path, string baseDirectory)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    /// <summary>
    /// True if <paramref name="path"/> equals <paramref name="directory"/> or is located inside it
    /// at any depth. Case-insensitive (Windows file system).
    /// </summary>
    public static bool IsUnder(string path, string directory)
    {
        var dir = WithTrailingSeparator(Path.GetFullPath(directory));
        var full = WithTrailingSeparator(Path.GetFullPath(path));
        return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if <paramref name="path"/> is under one of <paramref name="roots"/>: folders separated by ';'
    /// (the mod's "workspaceRoots" option, same rule as register.js). Empty entries, relative paths and
    /// entries that are not valid paths match nothing; no roots at all: nothing is under them.
    /// </summary>
    public static bool IsUnderAny(string path, string roots)
    {
        foreach (var part in roots.Split(';'))
        {
            var root = part.Trim();
            if (root.Length == 0)
                continue;
            try
            {
                // IsPathRooted throws on characters not valid in a path (.NET Framework), so it is inside the try.
                if (Path.IsPathRooted(root) && IsUnder(path, root))
                    return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                // Not a path: matches nothing.
            }
        }
        return false;
    }

    private static string WithTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}