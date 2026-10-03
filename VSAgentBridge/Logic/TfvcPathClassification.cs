namespace VSAgentBridge.Logic;

/// <summary>Result of classifying a path against the TFVC workspace cache.</summary>
internal sealed class TfvcPathClassification
{
    public static readonly TfvcPathClassification NotMapped = new(TfvcPathStatus.NotMapped, null, null, null);

    public TfvcPathClassification(
        TfvcPathStatus status,
        TfvcWorkspace? workspace,
        string? mappedRoot,
        string? cacheFilePath)
    {
        Status = status;
        Workspace = workspace;
        MappedRoot = mappedRoot;
        CacheFilePath = cacheFilePath;
    }

    public TfvcPathStatus Status { get; }

    /// <summary>The workspace whose mapping contains the path; null when not mapped.</summary>
    public TfvcWorkspace? Workspace { get; }

    /// <summary>The matched mapping root, normalized; null when not mapped.</summary>
    public string? MappedRoot { get; }

    /// <summary>The cache file the matched workspace entry came from; null when not mapped.</summary>
    public string? CacheFilePath { get; }
}