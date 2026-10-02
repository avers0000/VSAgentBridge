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

    private static string WithTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}