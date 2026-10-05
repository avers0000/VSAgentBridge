namespace VSAgentBridge.Logic;

/// <summary>
/// Texts the model sees for a new file: denials before the tool runs, and notes appended to the tool's result
/// after it ran. Denials follow the slice 1 form (cause, "not created", no retry with any tool, what to tell
/// the user); a text that starts with "X is not working" makes the model look for a workaround (slice 1, §3f).
/// Denials are temporary: slice 3 replaces them with redirection to Output.
/// </summary>
internal static class NewFileMessages
{
    private const string AddByHand =
        "right-click its folder in Solution Explorer, Add > Existing Item";

    /// <summary>Response to "before" when the file must not be created.</summary>
    public static string ForDenial(NewFileDecision decision)
    {
        var (cause, userAction) = Explain(decision);
        return
            $"VS Agent Bridge blocked creating this file: {cause} The file was not created. " +
            "Do not retry creating it with any tool and do not write its content to another file or location. " +
            "Show the intended file content in your reply, and at the end of your answer tell the user: " +
            userAction + ", then ask to repeat the change.";
    }

    /// <summary>"after": the file was added to the project; Visual Studio made it a pending add.</summary>
    public static string AddedToProject(string path, string projectName) =>
        $"VS Agent Bridge added {path} to project '{projectName}' in Visual Studio (pending add in TFVC). " +
        "Do not edit the project file to include it.";

    /// <summary>"after": created in place, but outside all projects of the solution.</summary>
    public static string OutsideProjects(string path) =>
        $"VS Agent Bridge: {path} was created, but it lies outside the folders of all projects of the open solution, " +
        "so it is in no project and not in TFVC. Do not edit project files to include it. " +
        "At the end of your answer tell the user: the new file is in no project and not in TFVC; " +
        $"if it is needed, add it in Visual Studio ({AddByHand}).";

    /// <summary>"after": the decision taken again no longer allows adding (e.g. the solution was closed meanwhile).</summary>
    public static string NotAdded(string path, NewFileDecision decision)
    {
        var (cause, _) = Explain(decision);
        return NotAddedText(path, cause);
    }

    /// <summary>"after": adding through Visual Studio was attempted and failed.</summary>
    public static string AddFailed(string path, string projectName, AddToProjectResult result)
    {
        var detail = result.Error != null ? $"Visual Studio reported an error ({result.Error})."
            : result.IsCopy ? $"Visual Studio added a copy at {result.AddedItemPath} instead of the file itself; that copy must be removed from the project and deleted."
            : !result.ItemFound ? "the project does not list the file after the call."
            : "the project could not be saved.";

        return NotAddedText(path, $"adding it to project '{projectName}' failed: {detail}");
    }

    private static string NotAddedText(string path, string cause) =>
        $"VS Agent Bridge: {path} was created, but it was not added to a project: {cause} " +
        "It is in no project and not in TFVC. Do not create it again and do not edit project files to include it. " +
        "At the end of your answer tell the user: the new file was not added to a project; " +
        $"add it in Visual Studio ({AddByHand}).";

    // Cause for the model, and what the user should do before repeating the change.
    private static (string Cause, string UserAction) Explain(NewFileDecision d) => d.Reason switch
    {
        NewFileReason.NotUnderOpenSolution => (
            "the folder is in a TFVC server workspace, but no open Visual Studio solution contains it.",
            "open the solution that contains this folder in Visual Studio (Visual Studio and the agent must run " +
            "at the same elevation level: both as administrator or both not)"),

        NewFileReason.SeveralInstances => (
            $"several Visual Studio instances have a solution open over this folder ({Join(d.Instances)}), " +
            "so it is not clear which one to add the file through.",
            "keep the solution open in one Visual Studio instance only"),

        NewFileReason.VisualStudioError => (
            $"a Visual Studio instance did not answer ({Join(d.Instances)}); it may be busy or was just closed.",
            "check that Visual Studio is responsive"),

        NewFileReason.AlreadyInProject => (
            "a project already lists this file, but it does not exist on disk " +
            "(it may have been deleted locally or not yet downloaded from TFS).",
            "get the latest version from TFS or check the item in Solution Explorer"),

        NewFileReason.SeveralProjectsInFolder => (
            $"several projects share this folder ({Join(d.ProjectsInFolder.Select(p => p.Name))}), " +
            "so it is not clear which one should get the file.",
            "create the file and add it to the right project by hand, or say which project it belongs to"),

        NewFileReason.ProjectUnloaded => (
            $"project '{d.Project?.Name}' is unloaded in Visual Studio.",
            $"reload project '{d.Project?.Name}' in Visual Studio"),

        _ => (
            $"the new file could not be prepared ({d.Reason}).",
            "check the log of VS Agent Bridge"),
    };

    private static string Join(IEnumerable<string> items) => string.Join(", ", items);
}