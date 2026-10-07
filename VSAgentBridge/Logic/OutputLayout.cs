namespace VSAgentBridge.Logic;

/// <summary>
/// Where a redirected file goes under Output (slice 3 results, §1.3–1.4). Pure path logic, no disk access:
/// Output\&lt;session&gt;\&lt;path relative to cwd&gt;, or Output\&lt;session&gt;\_outside\&lt;drive&gt;\&lt;path&gt; for a path outside cwd.
/// The same file of the same session always maps to the same place: writes and reads meet at one copy.
/// </summary>
internal static class OutputLayout
{
    /// <summary>Session folder when the request has no session.</summary>
    public const string NoSessionFolder = "_no-session";

    /// <summary>Under the session folder: paths outside the agent's working folder, by drive.</summary>
    public const string OutsideFolder = "_outside";

    /// <summary>Under <see cref="OutsideFolder"/>: network paths (\\server\share\...).</summary>
    public const string UncFolder = "_unc";

    /// <summary>Written once into each session folder: where its files come from.</summary>
    public const string SessionInfoFileName = "_vsab-session.txt";

    /// <summary>Appended to in each session folder: the deletes and moves the bridge refused (slice 4 results, §1.6).</summary>
    public const string PendingOperationsFileName = "_pending-operations.txt";

    // Long enough for any real session id (OpenCode: "ses_" + 26 characters), short enough to keep paths short.
    private const int MaxSessionFolderLength = 64;

    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>Folder of the session under <paramref name="outputDir"/>.</summary>
    public static string SessionFolder(string outputDir, string? session) =>
        Path.Combine(outputDir, SessionFolderName(session));

    /// <summary>Where <paramref name="path"/> (absolute, normalized) goes for this session.</summary>
    /// <param name="cwd">Agent's working folder; null puts every path under <see cref="OutsideFolder"/>.</param>
    public static string TargetPath(string outputDir, string? session, string? cwd, string path) =>
        Path.Combine(SessionFolder(outputDir, session), RelativePath(path, cwd));

    /// <summary>
    /// True for a path inside Output itself (the model edits or reads a copy by its real path).
    /// Such a path is never redirected again: it would end up under _outside of its own session.
    /// </summary>
    public static bool IsInOutput(string path, string outputDir) => PathUtil.IsUnder(path, outputDir);

    /// <summary>
    /// Session id as a single folder name: characters Windows does not allow in names become '_';
    /// trailing dots and spaces (which Windows silently drops) are removed; "." and ".." cannot result.
    /// </summary>
    public static string SessionFolderName(string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return NoSessionFolder;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = session!.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        var name = new string(chars).TrimEnd('.', ' ');

        if (name.Length > MaxSessionFolderLength)
            name = name.Substring(0, MaxSessionFolderLength).TrimEnd('.', ' ');

        return name.Length == 0 ? NoSessionFolder : name;
    }

    /// <summary>
    /// Path under the session folder: relative to <paramref name="cwd"/> when the file is inside it,
    /// otherwise _outside\D\... (drive letter as a folder) or _outside\_unc\server\share\....
    /// </summary>
    public static string RelativePath(string path, string? cwd)
    {
        var full = Path.GetFullPath(path);

        if (cwd != null)
        {
            var folder = Path.GetFullPath(cwd).TrimEnd(Separators) + Path.DirectorySeparatorChar;
            if (full.Length > folder.Length && full.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return full.Substring(folder.Length);
        }

        return Path.Combine(OutsideFolder, Outside(full));
    }

    private static string Outside(string full)
    {
        // \\server\share\dir\file → _unc\server\share\dir\file
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return Path.Combine(UncFolder, full.TrimStart(Separators));

        // D:\dir\file → D\dir\file
        var root = Path.GetPathRoot(full) ?? "";
        var drive = root.TrimEnd(Separators).TrimEnd(':');
        return Path.Combine(drive, full.Substring(root.Length));
    }
}