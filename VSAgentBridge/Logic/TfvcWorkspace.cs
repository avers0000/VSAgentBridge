namespace VSAgentBridge.Logic;

/// <summary>One workspace entry from a TFVC workspace cache file (VersionControl.config).</summary>
internal sealed class TfvcWorkspace
{
    public TfvcWorkspace(
        string serverUri,
        string name,
        string ownerName,
        string computer,
        bool isLocalWorkspace,
        IReadOnlyList<string> mappedPaths)
    {
        ServerUri = serverUri;
        Name = name;
        OwnerName = ownerName;
        Computer = computer;
        IsLocalWorkspace = isLocalWorkspace;
        MappedPaths = mappedPaths;
    }

    public string ServerUri { get; }
    public string Name { get; }
    public string OwnerName { get; }
    public string Computer { get; }
    public bool IsLocalWorkspace { get; }

    /// <summary>Local root folders of the workspace mappings, as stored in the cache (not normalized).</summary>
    public IReadOnlyList<string> MappedPaths { get; }
}