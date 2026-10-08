namespace VSAgentBridge.Logic;

/// <summary>
/// Texts the model sees when the hook redirects a file to the session folder under Output (slice 3 results, §1.6, §4.1).
/// A redirected write succeeds, so the texts do not say "blocked". The first text of a file in a session carries the
/// full instructions: that the file went to a session copy, that the original is unchanged, that the session keeps
/// using the original path (reads and edits go to the copy), that nothing is written elsewhere, and what the user does.
/// Later texts of the same file in the session are one line: the full instructions are already in the context.
/// The copy path appears once per text, in the part the model passes on to the user, together with the bridge's name:
/// the model itself keeps using the original path (step B, §3c: with the path only at the start, the model told the
/// user about "a copy that OpenCode keeps", without its path). "Merge … by comparing them, not by copying over it":
/// a bare "merge" came back as "replace the old file". Cleaning up Output is the user's business: no "delete the copy".
/// Causes and fixes come from <see cref="HookMessages"/> (existing files) and <see cref="NewFileMessages"/> (new files).
/// A fix is what the user does before merging (and so also avoids the redirect next time): "open the solution…".
/// TFVC is mentioned only when the path is under TFVC: the logic is the same for any version control.
/// </summary>
internal static class RedirectMessages
{
    internal const string AddByHand = "Add > Existing Item";

    private const string KeepWorking =
        "Keep using the original path: this session's reads and edits of it go to the copy; " +
        "do not write it anywhere else with any tool. ";

    private const string MergeByComparing = "merge it into the original by comparing them, not by copying over it.";

    /// <summary>
    /// "in no project and not in TFVC" under TFVC, "in no project" otherwise: a new file that Visual Studio did not add
    /// is also not a pending add. Shared with <see cref="NewFileMessages"/>.
    /// </summary>
    internal static string InNoProject(bool underTfvc) =>
        underTfvc ? "in no project and not in TFVC" : "in no project";

    /// <summary>"before": an existing file is edited in a copy under Output (first time in this session).</summary>
    /// <param name="fix">What the user does before merging; null when there is nothing to do.</param>
    public static string ExistingFile(string copy, string cause, string? fix) =>
        $"VS Agent Bridge saved this change to a session copy instead: {cause} The original is unchanged. " + KeepWorking +
        $"At the end of your answer tell the user: VS Agent Bridge saved the change to {copy}; " +
        Then(fix) + MergeByComparing;

    /// <summary>"before": a file the model creates is created under Output instead (first time in this session).</summary>
    /// <param name="addToProject">
    /// True: the user adds the copy to the project through Add > Existing Item (Visual Studio copies it into the folder).
    /// False: the project already lists the file (it is missing on disk); the fix says to restore it,
    /// then the user merges the copy into it.
    /// </param>
    /// <param name="underTfvc">The original path is in a TFVC workspace: the text also says "not in TFVC".</param>
    public static string NewFile(string copy, string cause, string? fix, bool addToProject, bool underTfvc) =>
        $"VS Agent Bridge created this file in a session copy instead: {cause} Nothing was created at the original path. " +
        KeepWorking +
        $"At the end of your answer tell the user: VS Agent Bridge created the new file as {copy}; " +
        $"it is {InNoProject(underTfvc)}; " +
        Then(fix) +
        (addToProject
            ? $"in Solution Explorer right-click the folder where it belongs, {AddByHand}, and pick that file " +
              "(Visual Studio copies it there)."
            : MergeByComparing);

    /// <summary>"before": a write of a file that already has a copy in this session (reason SessionCopy).</summary>
    public static string SessionCopy(string copy) =>
        $"VS Agent Bridge: saved to the session copy {copy}, not to the original.";

    /// <summary>"read": appended to the result of reading a file that has a copy in this session.</summary>
    public static string Read(string copy) =>
        $"VS Agent Bridge: read from the session copy {copy}, not from the original.";

    /// <summary>
    /// "after": a new file was created in place but could not be added to a project; it stays in place
    /// and a copy went to Output, so the rest of the session works on the copy.
    /// </summary>
    /// <param name="copy">The copy under Output; null when copying failed (then <paramref name="copyError"/> says why).</param>
    /// <param name="underTfvc">The file is in a TFVC workspace: the text also says "not in TFVC".</param>
    public static string CreatedNotAdded(string cause, string? fix, string? copy, string? copyError, bool underTfvc)
    {
        var start =
            $"VS Agent Bridge: the file was created but not added to a project: {cause} " +
            $"It is {InNoProject(underTfvc)}; do not create it again or edit project files to include it. ";

        if (copy == null)
        {
            return start +
                   $"(Copying it to the bridge's Output folder failed: {copyError}.) " +
                   "At the end of your answer tell the user: the new file is not in a project; " +
                   Then(fix) + $"add it in Visual Studio ({AddByHand}).";
        }

        return start + "The bridge also saved a session copy of it. " + KeepWorking +
               $"At the end of your answer tell the user: the new file is not in a project; " +
               $"VS Agent Bridge keeps its latest version in {copy}; " +
               Then(fix) + $"add it in Visual Studio ({AddByHand}) and merge the copy into it by comparing them.";
    }

    /// <summary>"before": the redirect itself failed (disk error under Output). A denial in the slice 1 form.</summary>
    /// <param name="isNewFile">The model was creating the file (true) or editing an existing one (false).</param>
    public static string OutputFailed(string cause, string error, bool isNewFile) =>
        (isNewFile
            ? $"VS Agent Bridge blocked creating this file: {cause} Saving it to the bridge's Output folder failed too ({error}). " +
              "Do not retry with any tool and do not write the content anywhere else. " +
              "Show the intended file content in your reply, and at the end of your answer tell the user: "
            : $"VS Agent Bridge blocked this edit: {cause} Saving it to the bridge's Output folder failed too ({error}). " +
              "The file was not changed. Do not retry with any tool and do not write the change anywhere else. " +
              "Show the intended change in your reply as a diff, and at the end of your answer tell the user: ") +
        $"VS Agent Bridge could not save to its Output folder ({error}); check its outputDir setting and its log.";

    private static string Then(string? fix) =>
        string.IsNullOrEmpty(fix) ? "" : fix + ", then ";
}