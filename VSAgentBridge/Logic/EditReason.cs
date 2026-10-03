namespace VSAgentBridge.Logic;

/// <summary>Why a decision was made; used for the log and for the message to the model.</summary>
internal enum EditReason
{
    /// <summary>The file does not exist yet (new files are handled separately).</summary>
    NewFile,

    /// <summary>The file is writable: git, no source control, local workspace, or made writable on purpose.</summary>
    Writable,

    /// <summary>Read-only and not inside any TFVC workspace of this machine.</summary>
    ReadOnlyNotTfvc,

    /// <summary>Read-only inside a local workspace, where TFVC does not set read-only: someone else did.</summary>
    ReadOnlyInLocalWorkspace,

    /// <summary>In a server workspace, but no open Visual Studio solution contains the file.</summary>
    NotInOpenSolution,

    /// <summary>Visual Studio could not be asked (busy, closed meanwhile, COM error).</summary>
    VisualStudioError,

    /// <summary>Visual Studio treats the file as not under source control (e.g. Exclude from Source Control).</summary>
    ExcludedFromSourceControl,

    /// <summary>Already checked out, yet read-only: checkout would not help.</summary>
    CheckedOutButReadOnly,

    /// <summary>Read-only TFVC file that Visual Studio controls: needs checkout.</summary>
    NeedsCheckout,
}