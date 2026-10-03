namespace VSAgentBridge.Logic;

/// <summary>
/// Decides whether a local path lies inside a TFVC workspace of this machine,
/// using only the workspace cache (no server, no Visual Studio).
/// Answers "is this place a TFVC workspace", not "is this file in TFS".
/// </summary>
internal static class TfvcPathClassifier
{
    /// <param name="fullPath">Absolute, normalized path of the file or folder.</param>
    /// <param name="machineName">Usually Environment.MachineName.</param>
    public static TfvcPathClassification Classify(
        string fullPath,
        string machineName,
        IReadOnlyList<TfvcCacheFile> cacheFiles)
    {
        TfvcPathClassification? best = null;
        var bestLength = -1;

        foreach (var (workspace, file) in LatestWorkspaces(machineName, cacheFiles))
        {
            foreach (var mapped in workspace.MappedPaths)
            {
                var root = NormalizeRoot(mapped);
                if (root == null || !IsUnder(fullPath, root))
                    continue;

                // Longest root wins: the most specific mapping owns the path.
                if (root.Length > bestLength)
                {
                    bestLength = root.Length;
                    best = new TfvcPathClassification(
                        workspace.IsLocalWorkspace ? TfvcPathStatus.LocalWorkspace : TfvcPathStatus.ServerWorkspace,
                        workspace,
                        root,
                        file.FilePath);
                }
            }
        }

        return best ?? TfvcPathClassification.NotMapped;
    }

    /// <summary>
    /// Workspaces of this machine, one entry per workspace (server + name + owner).
    /// When several cache files list the same workspace, the newest file wins:
    /// another Visual Studio version may not have refreshed its copy yet.
    /// </summary>
    private static IEnumerable<(TfvcWorkspace Workspace, TfvcCacheFile File)> LatestWorkspaces(
        string machineName,
        IReadOnlyList<TfvcCacheFile> cacheFiles)
    {
        var latest = new Dictionary<string, (TfvcWorkspace Workspace, TfvcCacheFile File)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in cacheFiles.Where(f => f.Error == null))
        {
            foreach (var workspace in file.Workspaces)
            {
                if (!string.Equals(workspace.Computer, machineName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = workspace.ServerUri + "|" + workspace.Name + "|" + workspace.OwnerName;
                if (!latest.TryGetValue(key, out var existing) || file.LastWriteTimeUtc > existing.File.LastWriteTimeUtc)
                    latest[key] = (workspace, file);
            }
        }

        return latest.Values;
    }

    private static string? NormalizeRoot(string mappedPath)
    {
        try
        {
            // GetFullPath first, then trim: trimming "D:\" first would give "D:", which means "current folder on D:".
            return Path.GetFullPath(mappedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return null; // unusable entry in the cache; ignore it
        }
    }

    private static bool IsUnder(string fullPath, string root) =>
        string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}