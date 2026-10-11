namespace VSAgentBridge.Logic;

/// <summary>
/// Texts of the MCP server "vsab" that the model sees (slice 5 results, §1.6): the create_file tool's description
/// and its answers when it does not create the file. A created file gets the hook's texts
/// (<see cref="NewFileMessages.AddedToProject"/>, <see cref="NewFileMessages.OutsideProjects"/>), causes come from
/// <see cref="NewFileMessages.Explain"/>: whichever path created the file, the model reads the same.
/// Every refusal says that nothing was created and which built-in tool to use: the hook handles the rest
/// (§1.1: create_file has no agent session, so it never writes to Output).
/// </summary>
internal static class McpMessages
{
    public const string ServerName = "vsab";
    public const string CreateFileTool = "create_file";

    public const string CreateFileDescription =
        "Create a new file and add it to its Visual Studio project, the way Visual Studio does it " +
        "(including source control). Use it for every new file instead of the write tool. " +
        "It never overwrites: change an existing file with the edit tool. " +
        "If it answers that you should use the write tool, do exactly that: nothing was created.";

    public const string PathDescription =
        "Path of the new file: absolute, or relative to your working directory.";

    public const string ContentDescription =
        "Full text of the file. The bridge sets its line endings and encoding to match the project.";

    /// <summary>The case belongs to the hook (it needs Output or the agent's session): the model uses write.</summary>
    /// <param name="cause">A sentence ending with a period, as from <see cref="NewFileMessages.Explain"/>.</param>
    public static string UseWrite(string cause) =>
        $"VS Agent Bridge did not create this file: {cause} Nothing was created. " +
        "Create it with the write tool instead; the bridge handles the new file there.";

    public const string InOutput = "the path is in VS Agent Bridge's Output folder.";

    public const string OutsideRoots = "the path is outside VS Agent Bridge's workspace roots.";

    public static string WriteFailed(string error) => $"writing the file failed ({error}).";

    public const string InternalError = "the bridge failed (details are in its log).";

    public const string AlreadyExists =
        "VS Agent Bridge did not create this file: it already exists. Nothing was changed. " +
        "Change it with the edit tool.";

    public const string FolderExists =
        "VS Agent Bridge did not create this file: a folder with this path exists. Nothing was created.";

    public static string InvalidPath(string error) =>
        $"VS Agent Bridge did not create this file: the path is not valid ({error}). Nothing was created.";
}