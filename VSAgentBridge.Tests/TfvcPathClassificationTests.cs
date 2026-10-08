using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class TfvcPathClassificationTests
{
    // Server and local workspaces differ only in the checkout of existing files; texts treat both as "under TFVC".
    [TestMethod]
    [DataRow(TfvcPathStatus.NotMapped, false)]
    [DataRow(TfvcPathStatus.ServerWorkspace, true)]
    [DataRow(TfvcPathStatus.LocalWorkspace, true)]
    public void IsUnderTfvc_AnyWorkspace(TfvcPathStatus status, bool expected)
    {
        Assert.AreEqual(expected, new TfvcPathClassification(status, null, null, null).IsUnderTfvc);
    }

    [TestMethod]
    public void NotMapped_IsNotUnderTfvc()
    {
        Assert.IsFalse(TfvcPathClassification.NotMapped.IsUnderTfvc);
    }
}
