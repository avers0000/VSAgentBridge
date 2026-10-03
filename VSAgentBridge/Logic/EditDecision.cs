namespace VSAgentBridge.Logic;

/// <summary>Decision for an edit of an existing file, with the facts it was based on (for the log).</summary>
internal sealed class EditDecision
{
    public EditDecision(EditAction action, EditReason reason, TfvcPathClassification tfvc, VsFileFacts? vs)
    {
        Action = action;
        Reason = reason;
        Tfvc = tfvc;
        Vs = vs;
    }

    public EditAction Action { get; }
    public EditReason Reason { get; }
    public TfvcPathClassification Tfvc { get; }

    /// <summary>Null when Visual Studio was not asked (the decision did not need it).</summary>
    public VsFileFacts? Vs { get; }
}