using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class EditDeciderTests
{
    [TestMethod]
    [DataRow(false, true, TfvcPathStatus.ServerWorkspace, EditAction.None, EditReason.NewFile)]
    [DataRow(true, false, TfvcPathStatus.ServerWorkspace, EditAction.None, EditReason.Writable)]
    [DataRow(true, false, TfvcPathStatus.NotMapped, EditAction.None, EditReason.Writable)]
    [DataRow(true, true, TfvcPathStatus.NotMapped, EditAction.Redirect, EditReason.ReadOnlyNotTfvc)]
    [DataRow(true, true, TfvcPathStatus.LocalWorkspace, EditAction.Redirect, EditReason.ReadOnlyInLocalWorkspace)]
    public void Decide_WithoutAskingVisualStudio(
        bool exists, bool isReadOnly, TfvcPathStatus status, EditAction expectedAction, EditReason expectedReason)
    {
        var calls = 0;

        var decision = EditDecider.Decide(exists, isReadOnly, Classification(status), () =>
        {
            calls++;
            return VsFileFacts.NotFound;
        });

        Assert.AreEqual(expectedAction, decision.Action);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(0, calls);
        Assert.IsNull(decision.Vs);
    }

    [TestMethod]
    [DataRow(true, true, false, "RPC_E_CALL_REJECTED", EditAction.Redirect, EditReason.VisualStudioError)] // error beats the flags
    [DataRow(false, false, false, null, EditAction.Redirect, EditReason.NotInOpenSolution)]
    [DataRow(true, false, false, null, EditAction.Redirect, EditReason.ExcludedFromSourceControl)]
    [DataRow(true, true, true, null, EditAction.Redirect, EditReason.CheckedOutButReadOnly)]
    [DataRow(true, true, false, null, EditAction.Checkout, EditReason.NeedsCheckout)]
    public void Decide_ReadOnlyInServerWorkspace_AsksVisualStudioOnce(
        bool instanceFound, bool isUnderScc, bool isCheckedOut, string? error,
        EditAction expectedAction, EditReason expectedReason)
    {
        var tfvc = Classification(TfvcPathStatus.ServerWorkspace);
        var facts = new VsFileFacts(instanceFound, isUnderScc, isCheckedOut, error);
        var calls = 0;

        var decision = EditDecider.Decide(exists: true, isReadOnly: true, tfvc, () =>
        {
            calls++;
            return facts;
        });

        Assert.AreEqual(expectedAction, decision.Action);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(1, calls);
        Assert.AreSame(facts, decision.Vs);
        Assert.AreSame(tfvc, decision.Tfvc);
    }

    private static TfvcPathClassification Classification(TfvcPathStatus status) =>
        new(status, workspace: null, mappedRoot: null, cacheFilePath: null);
}
