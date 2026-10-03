namespace VSAgentBridge.Logic;

/// <summary>
/// Decides what to do before the agent edits an existing file.
/// Pure logic: file facts and the cache classification are passed in;
/// Visual Studio is asked through a callback, and only when the decision needs it.
/// </summary>
internal static class EditDecider
{
    /// <param name="exists">The file exists on disk.</param>
    /// <param name="isReadOnly">The file has the read-only attribute.</param>
    /// <param name="tfvc">Classification of the path against the TFVC workspace cache.</param>
    /// <param name="queryVisualStudio">Asks Visual Studio about the file; called at most once.</param>
    public static EditDecision Decide(
        bool exists,
        bool isReadOnly,
        TfvcPathClassification tfvc,
        Func<VsFileFacts> queryVisualStudio)
    {
        if (!exists)
            return Make(EditAction.None, EditReason.NewFile, tfvc);

        if (!isReadOnly)
            return Make(EditAction.None, EditReason.Writable, tfvc);

        switch (tfvc.Status)
        {
            case TfvcPathStatus.NotMapped:
                return Make(EditAction.Redirect, EditReason.ReadOnlyNotTfvc, tfvc);

            case TfvcPathStatus.LocalWorkspace:
                return Make(EditAction.Redirect, EditReason.ReadOnlyInLocalWorkspace, tfvc);

            case TfvcPathStatus.ServerWorkspace:
                return DecideInServerWorkspace(tfvc, queryVisualStudio());

            default:
                throw new ArgumentOutOfRangeException(nameof(tfvc), tfvc.Status, "Unknown TFVC path status.");
        }
    }

    private static EditDecision DecideInServerWorkspace(TfvcPathClassification tfvc, VsFileFacts vs)
    {
        if (vs.Error != null)
            return Make(EditAction.Redirect, EditReason.VisualStudioError, tfvc, vs);

        if (!vs.InstanceFound)
            return Make(EditAction.Redirect, EditReason.NotInOpenSolution, tfvc, vs);

        if (!vs.IsUnderScc)
            return Make(EditAction.Redirect, EditReason.ExcludedFromSourceControl, tfvc, vs);

        if (vs.IsCheckedOut)
            return Make(EditAction.Redirect, EditReason.CheckedOutButReadOnly, tfvc, vs);

        return Make(EditAction.Checkout, EditReason.NeedsCheckout, tfvc, vs);
    }

    private static EditDecision Make(EditAction action, EditReason reason, TfvcPathClassification tfvc, VsFileFacts? vs = null) =>
        new(action, reason, tfvc, vs);
}