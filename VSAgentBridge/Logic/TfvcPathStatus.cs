namespace VSAgentBridge.Logic;

/// <summary>Where a local path stands relative to the TFVC workspaces of this machine.</summary>
internal enum TfvcPathStatus
{
    /// <summary>Not inside any workspace mapping of this machine (git, no source control, other machine's workspace).</summary>
    NotMapped,

    /// <summary>Inside a server workspace: files are read-only until checked out.</summary>
    ServerWorkspace,

    /// <summary>Inside a local workspace: files are writable, no checkout needed.</summary>
    LocalWorkspace,
}