namespace VSAgentBridge.Logic;

/// <summary>What <see cref="OutputStore"/> did to prepare a redirected path. Plain data for the hook, the log and the messages.</summary>
internal sealed class OutputResult
{
    public OutputResult(string targetPath, bool copied, bool reused, string? error)
    {
        TargetPath = targetPath;
        Copied = copied;
        Reused = reused;
        Error = error;
    }

    public static OutputResult Failed(string targetPath, string error) =>
        new(targetPath, copied: false, reused: false, error);

    /// <summary>The path under Output the tool must use.</summary>
    public string TargetPath { get; }

    /// <summary>A new copy was made by this call.</summary>
    public bool Copied { get; }

    /// <summary>The file already existed under Output (an earlier redirect of this session).</summary>
    public bool Reused { get; }

    /// <summary>Why the path could not be prepared; the redirect must not happen then.</summary>
    public string? Error { get; }

    public bool Succeeded => Error == null;

    /// <summary>For the log: "new", "reused" or "none" (a new file: nothing to copy).</summary>
    public string CopyState => Copied ? "new" : Reused ? "reused" : "none";
}