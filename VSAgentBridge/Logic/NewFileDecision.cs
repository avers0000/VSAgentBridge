namespace VSAgentBridge.Logic;

/// <summary>
/// Decision for a file that does not exist yet, with the facts it was based on (for the log and the messages).
/// Which of the optional values are set depends on <see cref="Reason"/>.
/// </summary>
internal sealed class NewFileDecision
{
    public NewFileDecision(
        NewFileAction action,
        NewFileReason reason,
        TfvcPathClassification tfvc,
        IReadOnlyList<VsSolutionFacts>? solutions = null,
        int solutionIndex = -1,
        IReadOnlyList<string>? instances = null,
        VsProjectFacts? project = null,
        IReadOnlyList<string>? folderChain = null,
        IReadOnlyList<VsProjectFacts>? projectsInFolder = null)
    {
        Action = action;
        Reason = reason;
        Tfvc = tfvc;
        Solutions = solutions;
        SolutionIndex = solutionIndex;
        Instances = instances ?? Array.Empty<string>();
        Project = project;
        FolderChain = folderChain ?? Array.Empty<string>();
        ProjectsInFolder = projectsInFolder ?? Array.Empty<VsProjectFacts>();
    }

    public NewFileAction Action { get; }
    public NewFileReason Reason { get; }
    public TfvcPathClassification Tfvc { get; }

    /// <summary>What every Visual Studio instance reported; null when Visual Studio was not asked.</summary>
    public IReadOnlyList<VsSolutionFacts>? Solutions { get; }

    /// <summary>Index in <see cref="Solutions"/> of the working instance (step 1 found exactly one); -1 otherwise.</summary>
    public int SolutionIndex { get; }

    /// <summary>
    /// Labels of the instances behind a step-1 refusal: those whose solution covers the path (SeveralInstances)
    /// or those that did not answer (VisualStudioError). For the message and the log.
    /// </summary>
    public IReadOnlyList<string> Instances { get; }

    /// <summary>The project whose folder contains the path (InProjectFolder, ProjectUnloaded).</summary>
    public VsProjectFacts? Project { get; }

    /// <summary>
    /// Folder names from the project folder down to the file's folder, outermost first;
    /// empty when the file lies directly in the project folder. Each level is looked up in the project
    /// and added with AddFolder if missing — like Add → New Folder before Add → Existing Item by hand.
    /// </summary>
    public IReadOnlyList<string> FolderChain { get; }

    /// <summary>The different projects that share the deepest project folder (SeveralProjectsInFolder).</summary>
    public IReadOnlyList<VsProjectFacts> ProjectsInFolder { get; }

    /// <summary>The response to "before" asks the adapter for an "after" call.</summary>
    public bool RequestsAfter => Action == NewFileAction.AddToProject || Action == NewFileAction.LeaveOutsideProjects;
}