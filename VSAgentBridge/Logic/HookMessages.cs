namespace VSAgentBridge.Logic;

/// <summary>
/// Causes for an existing file that cannot be edited in place, and the remaining denial texts.
/// Since slice 3 every EditReason that stops an in-place edit, and a failed checkout, lead to a redirect into Output:
/// <see cref="RedirectMessages"/> builds those texts from <see cref="Explain"/> / <see cref="ExplainCheckoutFailure"/>.
/// Only an internal error of the bridge is still a denial (spec §4.6; slice 1 results, §3f: the form
/// "blocked … do not retry with any tool … show a diff … tell the user" keeps the model from looking for a workaround).
/// Texts are kept short (slice 3 results, §4.1): they go into the model's context on every call that produces them.
/// </summary>
internal static class HookMessages
{
    /// <summary>An existing file: the bridge failed with an exception while preparing the edit.</summary>
    public const string InternalError =
        "VS Agent Bridge blocked this edit: the bridge failed with an internal error. The file was not changed. " +
        "Do not retry with any tool and do not write the change anywhere else. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        "VS Agent Bridge failed with an internal error; details are in its log.";

    /// <summary>A new file: the bridge failed with an exception while preparing it.</summary>
    public const string InternalErrorNewFile =
        "VS Agent Bridge blocked creating this file: the bridge failed with an internal error. " +
        "Do not retry with any tool and do not write the content anywhere else. " +
        "Show the intended file content in your reply, and at the end of your answer tell the user: " +
        "VS Agent Bridge failed with an internal error; details are in its log.";

    /// <summary>The user's part when Visual Studio and the agent run at different elevation levels.</summary>
    public const string Elevation = "run it at the agent's elevation level";

    /// <summary>
    /// Why the file cannot be edited in place, and what the user does before merging the copy into the original
    /// (an imperative fragment without a final period; it also avoids the redirect next time).
    /// Fix is null when there is nothing for the user to do.
    /// </summary>
    public static (string Cause, string? Fix) Explain(EditReason reason) => reason switch
    {
        EditReason.ReadOnlyNotTfvc => (
            "the file is read-only and not in a TFVC workspace.",
            "make the original writable if it should be edited"),

        EditReason.ReadOnlyInLocalWorkspace => (
            "the file is read-only in a TFVC local workspace, where TFVC never sets read-only.",
            "find out why the original is read-only and make it writable"),

        EditReason.NotInOpenSolution => (
            "the file needs a TFVC checkout, but no open Visual Studio solution contains it.",
            $"open the solution with the file in Visual Studio ({Elevation})"),

        EditReason.VisualStudioError => (
            "Visual Studio did not answer (busy or just closed).",
            "make sure Visual Studio is open and responsive"),

        EditReason.ExcludedFromSourceControl => (
            "Visual Studio treats the file as excluded from source control, so it is not checked out automatically.",
            "if the change should go to TFS, check the original out manually in Visual Studio"),

        EditReason.CheckedOutButReadOnly => (
            "the file is checked out but still read-only.",
            "make the original writable (or undo the checkout and check it out again)"),

        _ => ($"the edit could not be prepared ({reason}).", null),
    };

    /// <summary>Why checking the file out failed, and what the user does about it.</summary>
    public static (string Cause, string? Fix) ExplainCheckoutFailure(CheckoutResult result)
    {
        var detail = result.Error != null ? $"Visual Studio reported an error ({result.Error})."
            : !result.IsWritable ? "the file is still read-only afterwards."
            : "Visual Studio does not report it as checked out.";

        return (
            "the checkout through Visual Studio failed: " + detail,
            "check the original's state in Visual Studio (Pending Changes)");
    }
}