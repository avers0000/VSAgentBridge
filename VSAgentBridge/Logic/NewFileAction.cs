namespace VSAgentBridge.Logic;

/// <summary>What the hook does when the agent is about to create a file that does not exist yet.</summary>
internal enum NewFileAction
{
    /// <summary>
    /// Do nothing: a project of the working solution already lists the path. In "before" the hook redirects
    /// that case (the file is missing on disk, so creating it in place would bypass the project and source control);
    /// in "after" it lets it pass (an SDK-style project picked the file up through a wildcard).
    /// </summary>
    None,

    /// <summary>
    /// Let the tool through, then add the new file to the project whose folder contains it
    /// (the response to "before" asks for an "after" call).
    /// </summary>
    AddToProject,

    /// <summary>
    /// Let the tool through without adding: the path is under an open solution but outside all its projects.
    /// The "after" call tells the model the file is in no project (and, under TFVC, not in TFVC).
    /// </summary>
    LeaveOutsideProjects,

    /// <summary>
    /// The file must not be created in place. In "before" the hook redirects the tool to the session folder
    /// under Output; in "after" (the file already exists) it leaves the file in place and copies it there.
    /// </summary>
    Redirect,
}