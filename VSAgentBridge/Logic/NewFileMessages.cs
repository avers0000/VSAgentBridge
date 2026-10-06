namespace VSAgentBridge.Logic;

/// <summary>
/// Texts and causes for a new file. Since slice 3 a file that must not be created in place goes to Output, and a file
/// created but not added is copied there: <see cref="RedirectMessages"/> builds those texts from <see cref="Explain"/>
/// and <see cref="ExplainAddFailure"/>. The two notes appended after a successful creation stay here.
/// Texts are kept short (slice 3 results, §4.1); the file path is not repeated (the model gave it).
/// </summary>
internal static class NewFileMessages
{
    /// <summary>"after": the file was added to the project; Visual Studio made it a pending add.</summary>
    public static string AddedToProject(string projectName) =>
        $"VS Agent Bridge added the file to project '{projectName}' (pending add in TFVC); " +
        "do not edit the project file to include it.";

    /// <summary>"after": created in place, but outside all projects of the solution.</summary>
    public const string OutsideProjects =
        "VS Agent Bridge: the file lies outside all projects of the open solution, so it is in no project and not in TFVC; " +
        "do not edit project files to include it. At the end of your answer tell the user: " +
        "the new file is in no project and not in TFVC; if it is needed, add it in Visual Studio (" +
        RedirectMessages.AddByHand + ").";

    /// <summary>
    /// Why the file cannot be created and added in place, and what the user does before putting the copy in place
    /// (an imperative fragment without a final period; null when there is nothing to do).
    /// Used both before the tool (redirect) and after it (not added).
    /// </summary>
    public static (string Cause, string? Fix) Explain(NewFileDecision d) => d.Reason switch
    {
        NewFileReason.NotUnderOpenSolution => (
            "the folder is in a TFVC workspace, but no open Visual Studio solution contains it.",
            $"open the solution with this folder in Visual Studio ({HookMessages.Elevation})"),

        NewFileReason.SeveralInstances => (
            $"several Visual Studio instances have a solution open over this folder ({DescribeInstances(d)}), " +
            "so the target is unclear.",
            "keep that solution open in one Visual Studio instance only"),

        NewFileReason.VisualStudioError => (
            $"Visual Studio did not answer ({DescribeInstances(d)}; busy or just closed).",
            "make sure Visual Studio is open and responsive"),

        NewFileReason.AlreadyInProject => (
            "a project already lists this file, but it is missing on disk (deleted locally or not yet got from TFS).",
            "get the latest version of the original from TFS"),

        NewFileReason.SeveralProjectsInFolder => (
            $"several projects share this folder ({string.Join(", ", d.ProjectsInFolder.Select(p => p.Name))}), " +
            "so the target project is unclear.",
            "decide which of them gets the file"),

        NewFileReason.ProjectUnloaded => (
            $"project '{d.Project?.Name}' is unloaded in Visual Studio.",
            "reload it"),

        _ => ($"the new file could not be prepared ({d.Reason}).", null),
    };

    /// <summary>"after": adding through Visual Studio was attempted and failed.</summary>
    public static (string Cause, string? Fix) ExplainAddFailure(string projectName, AddToProjectResult result)
    {
        if (result.IsCopy)
        {
            return (
                $"adding it to project '{projectName}' failed: Visual Studio added a copy at {result.AddedItemPath} instead.",
                "remove that copy from the project and delete it");
        }

        var detail = result.Error != null ? $"Visual Studio reported an error ({result.Error})."
            : !result.ItemFound ? "the project does not list the file afterwards."
            : "the project could not be saved.";

        return (
            $"adding it to project '{projectName}' failed: {detail}",
            result.ItemFound ? $"save project '{projectName}' in Visual Studio" : null);
    }

    // "VS2019 (pid 16616): App.sln, VS2022 (pid 9856)": the instances behind a step-1 refusal, with the solution
    // each has open when it answered. Labels come from the decision; solutions are looked up by label.
    private static string DescribeInstances(NewFileDecision d) =>
        string.Join(", ", d.Instances.Select(label =>
            VsInstanceLabel.Describe(label, d.Solutions?.FirstOrDefault(s => s.Instance == label)?.SolutionPath)));
}