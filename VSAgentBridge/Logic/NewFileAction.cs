namespace VSAgentBridge.Logic;

/// <summary>What the hook does when the agent is about to create a file that does not exist yet.</summary>
internal enum NewFileAction
{
    /// <summary>
    /// Let the tool through and do nothing afterwards: the path is not in a TFVC server workspace,
    /// or a project of an open solution already lists it (in "before" the hook redirects that case:
    /// the file is missing on disk, so creating it in place would bypass TFVC).
    /// </summary>
    None,

    /// <summary>
    /// Let the tool through, then add the new file to the project whose folder contains it
    /// (the response to "before" asks for an "after" call).
    /// </summary>
    AddToProject,

    /// <summary>
    /// Let the tool through without adding: the path is under an open solution but outside all its projects.
    /// The "after" call tells the model the file is in no project and not in TFVC.
    /// </summary>
    LeaveOutsideProjects,

    /// <summary>
    /// The file must not be created in place. In "before" the hook redirects the tool to the session folder
    /// under Output; in "after" (the file already exists) it leaves the file in place and copies it there.
    /// </summary>
    Redirect,
}