namespace VSAgentBridge.Logic;

/// <summary>Why a decision about a new file was made; used for the log and for the message to the model.</summary>
internal enum NewFileReason
{
    /// <summary>Not inside any TFVC workspace of this machine: the bridge does not interfere.</summary>
    NotTfvc,

    /// <summary>Inside a TFVC local workspace: pending adds are detected by TFVC itself.</summary>
    LocalWorkspace,

    /// <summary>No Visual Studio instance has a solution open whose folder contains the path.</summary>
    NotUnderOpenSolution,

    /// <summary>Several Visual Studio instances have a solution open whose folder contains the path.</summary>
    SeveralInstances,

    /// <summary>A Visual Studio instance did not answer: it may be the one with the solution.</summary>
    VisualStudioError,

    /// <summary>The solution already lists the path (an item whose file is missing, a wildcard of an SDK-style project).</summary>
    AlreadyInProject,

    /// <summary>Several different projects share the deepest project folder that contains the path.</summary>
    SeveralProjectsInFolder,

    /// <summary>The project whose folder contains the path is unloaded.</summary>
    ProjectUnloaded,

    /// <summary>The path is in the folder of a loaded project: the file is added to it.</summary>
    InProjectFolder,

    /// <summary>Under the solution folder, but outside the folders of all its projects: created, not added.</summary>
    OutsideProjects,
}