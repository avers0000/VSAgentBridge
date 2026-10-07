namespace VSAgentBridge.Logic;

/// <summary>
/// Texts the model sees when the hook refuses a shell command or a patch (slice 4 results, §1.5).
/// All in the denial form of slice 1 (§3f): "blocked … nothing was run … do not retry … what to do instead":
/// the form that keeps a weak model from looking for a workaround.
/// What the command does is said per operation (<see cref="Describe"/>); what the model does next depends on
/// the group: a delete or move has no tool the model could use instead, so it is listed for the user; a write,
/// a copy or making a file writable has one (write/edit), and the bridge then does the checkout and the adding
/// to the project. In v2 these operations are carried out instead of refused, with texts of their own.
/// The record file under Output appears once, in the part the model passes on to the user.
/// </summary>
internal static class ShellMessages
{
    /// <summary>The bridge failed with an exception while checking a command.</summary>
    public const string InternalError =
        "VS Agent Bridge blocked this command: the bridge failed with an internal error. Nothing was run. " +
        "Do not retry it with any tool. At the end of your answer tell the user: " +
        "VS Agent Bridge failed with an internal error; details are in its log.";

    /// <summary>A command that deletes, moves or renames files (it may also do other things to files).</summary>
    /// <param name="findings">Everything the scanner found in the command.</param>
    /// <param name="recordPath">The session's record of refused operations; null when writing it failed.</param>
    public static string DeleteOrMoveCommand(IReadOnlyList<ShellFinding> findings, string? recordPath) =>
        $"VS Agent Bridge blocked this command: {Describe(findings)}. Deleting, moving and renaming files is done " +
        "only by the user, in Visual Studio. Nothing was run. " +
        DeleteOrMoveRest(recordPath);

    /// <summary>A patch that deletes a file (*** Delete File) or moves it (*** Move to).</summary>
    /// <param name="operation"><see cref="ShellOperation.Delete"/> or <see cref="ShellOperation.Move"/>.</param>
    public static string DeleteOrMovePatch(ShellOperation operation, string? recordPath) =>
        $"VS Agent Bridge blocked this patch: it {Verb(operation)}. Deleting, moving and renaming files is done " +
        "only by the user, in Visual Studio. Nothing in the patch was applied; " +
        "apply its other changes again, without the delete and move parts. " +
        DeleteOrMoveRest(recordPath);

    /// <summary>A command that writes to files, copies them or makes them writable, and does not delete or move any.</summary>
    public static string WriteCommand(IReadOnlyList<ShellFinding> findings) =>
        $"VS Agent Bridge blocked this command: {Describe(findings)}. Nothing was run. " +
        "Do not change files with shell commands or scripts and do not make them writable: use the write tool " +
        "to create a file and the edit tool to change one. The bridge then checks the file out of TFVC " +
        "and adds a new file to its project.";

    /// <summary>
    /// "it deletes files (Remove-Item) and writes to files (&gt;, Set-Content)": each operation once, in the order
    /// first found, with the commands that do it.
    /// </summary>
    internal static string Describe(IReadOnlyList<ShellFinding> findings)
    {
        var parts = findings
            .GroupBy(f => f.Operation)
            .Select(g => $"{Verb(g.Key)} ({string.Join(", ", g.Select(f => f.Command).Distinct(StringComparer.OrdinalIgnoreCase))})")
            .ToList();

        return "it " + (parts.Count <= 1
            ? string.Concat(parts)
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1]);
    }

    internal static string Verb(ShellOperation operation) => operation switch
    {
        ShellOperation.Write => "writes to files",
        ShellOperation.Copy => "copies files",
        ShellOperation.ClearReadOnly => "makes files writable",
        ShellOperation.Delete => "deletes files",
        ShellOperation.Move => "moves or renames files",
        _ => "changes files",
    };

    private static string DeleteOrMoveRest(string? recordPath) =>
        "Do not retry it and do not do it another way (another command or tool, emptying files, " +
        "recreating them under new names). Continue with the rest of the task and do not change references " +
        "to these files yet. At the end of your answer tell the user which files to delete, move or rename " +
        "and where they are referenced: the user does it in Visual Studio (Solution Explorer; " +
        "for SQL objects, the SSDT Rename refactoring)" +
        (recordPath != null ? $"; VS Agent Bridge recorded the request in {recordPath}." : ".");
}