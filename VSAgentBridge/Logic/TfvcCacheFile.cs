namespace VSAgentBridge.Logic;

/// <summary>Result of reading one VersionControl.config: its workspaces, or why it could not be read.</summary>
internal sealed class TfvcCacheFile
{
    public TfvcCacheFile(string filePath, DateTime lastWriteTimeUtc, IReadOnlyList<TfvcWorkspace> workspaces, string? error)
    {
        FilePath = filePath;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Workspaces = workspaces;
        Error = error;
    }

    public string FilePath { get; }

    /// <summary>Used to prefer the newest file when the same workspace appears in several caches.</summary>
    public DateTime LastWriteTimeUtc { get; }

    public IReadOnlyList<TfvcWorkspace> Workspaces { get; }

    /// <summary>Null if the file was read and parsed; otherwise the reason it was skipped.</summary>
    public string? Error { get; }
}