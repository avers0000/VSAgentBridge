namespace VSAgentBridge.Logic;

/// <summary>
/// Texts the model sees when the hook denies an edit. Each one names the cause, forbids workarounds
/// and gives a concrete next step (spec §4.6). Temporary: slice 3 replaces denials with redirection to Output.
/// </summary>
internal static class HookMessages
{
    public static string InternalError => Blocked(
        "the bridge itself failed with an internal error.",
        "the edit was blocked by an internal error of VS Agent Bridge; details are in its log.");

    public static string ForRedirect(EditReason reason) => reason switch
    {
        EditReason.ReadOnlyNotTfvc => Blocked(
            "the file is read-only and is not in a TFVC workspace, so the bridge does not touch it.",
            "the file is read-only; make it writable if the change is intended, then ask to repeat the change."),

        EditReason.ReadOnlyInLocalWorkspace => Blocked(
            "the file is read-only inside a TFVC local workspace, where TFVC itself never sets read-only.",
            "something else made the file read-only; make it writable if the change is intended, then ask to repeat the change."),

        EditReason.NotInOpenSolution => Blocked(
            "the file is in a TFVC server workspace and needs a checkout, but no open Visual Studio solution contains it.",
            "open the solution that contains the file in Visual Studio (Visual Studio and the agent must run at the same " +
            "elevation level: both as administrator or both not), then ask to repeat the change."),

        EditReason.VisualStudioError => Blocked(
            "Visual Studio did not answer (it may be busy or was just closed).",
            "check that Visual Studio is responsive, then ask to repeat the change."),

        EditReason.ExcludedFromSourceControl => Blocked(
            "Visual Studio treats the file as excluded from source control, so it is not checked out automatically.",
            "if the change should go to TFS, check the file out manually in Visual Studio, then ask to repeat the change."),

        EditReason.CheckedOutButReadOnly => Blocked(
            "the file is already checked out but still read-only, so a checkout would not help.",
            "make the file writable (or undo the checkout and check it out again), then ask to repeat the change."),

        _ => Blocked(
            $"the edit could not be prepared ({reason}).",
            "the edit was blocked by VS Agent Bridge; details are in its log."),
    };

    public static string ForCheckoutFailure(CheckoutResult result)
    {
        var detail = result.Error != null ? $"Visual Studio reported an error ({result.Error})."
            : !result.IsWritable ? "the file is still read-only after the checkout call."
            : "Visual Studio does not report the file as checked out.";

        return Blocked(
            "checking the file out through Visual Studio failed: " + detail,
            "check the file out manually in Visual Studio (it may already be listed in Pending Changes), then ask to repeat the change.");
    }

    private static string Blocked(string cause, string userAction) =>
        $"VS Agent Bridge blocked this edit: {cause} The file was not changed. " +
        "Do not retry this edit and do not write the change to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " + userAction;
}