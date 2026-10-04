using System.Xml;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class TfvcWorkspaceCacheTests
{
    [TestMethod]
    public void Parse_ReadsCompleteEntries_SkipsIncompleteOnes()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <VersionControlServer>
              <Servers>
                <ServerInfo uri="http://tfs.example.test:8080/tfs/alpha">
                  <WorkspaceInfo name="DEVBOX-main" ownerName="EXAMPLE\jdoe" computer="DEVBOX" isLocalWorkspace="false" LastSavedCheckin="ignored">
                    <MappedPaths>
                      <MappedPath path="C:\Src\Alpha" />
                      <MappedPath path=" " />
                      <MappedPath path="C:\Src\Shared" />
                    </MappedPaths>
                  </WorkspaceInfo>
                  <WorkspaceInfo ownerName="EXAMPLE\jdoe" computer="DEVBOX">
                    <MappedPaths><MappedPath path="C:\Src\NoName" /></MappedPaths>
                  </WorkspaceInfo>
                  <WorkspaceInfo name="no-computer" ownerName="EXAMPLE\jdoe">
                    <MappedPaths><MappedPath path="C:\Src\NoComputer" /></MappedPaths>
                  </WorkspaceInfo>
                  <WorkspaceInfo name="no-mappings" computer="DEVBOX">
                    <MappedPaths />
                  </WorkspaceInfo>
                  <WorkspaceInfo name="blank-mappings" computer="DEVBOX">
                    <MappedPaths><MappedPath path="" /><MappedPath /></MappedPaths>
                  </WorkspaceInfo>
                </ServerInfo>
                <ServerInfo uri="http://tfs.example.test:8080/tfs/beta">
                  <WorkspaceInfo name="DEVBOX-beta" computer="DEVBOX" isLocalWorkspace="true">
                    <MappedPaths><MappedPath path="E:\Beta" /></MappedPaths>
                  </WorkspaceInfo>
                </ServerInfo>
              </Servers>
            </VersionControlServer>
            """;

        var result = TfvcWorkspaceCache.Parse(xml);

        Assert.HasCount(2, result);

        var main = result[0];
        Assert.AreEqual("http://tfs.example.test:8080/tfs/alpha", main.ServerUri);
        Assert.AreEqual("DEVBOX-main", main.Name);
        Assert.AreEqual(@"EXAMPLE\jdoe", main.OwnerName);
        Assert.AreEqual("DEVBOX", main.Computer);
        Assert.IsFalse(main.IsLocalWorkspace);
        CollectionAssert.AreEqual(new[] { @"C:\Src\Alpha", @"C:\Src\Shared" }, main.MappedPaths.ToArray());

        var beta = result[1];
        Assert.AreEqual("http://tfs.example.test:8080/tfs/beta", beta.ServerUri);
        Assert.AreEqual("", beta.OwnerName);
        Assert.IsTrue(beta.IsLocalWorkspace);
    }

    [TestMethod]
    [DataRow(null, false)] // attribute absent: pre-2012 cache, server workspaces only
    [DataRow("false", false)]
    [DataRow("true", true)]
    [DataRow("True", true)]
    public void Parse_IsLocalWorkspace(string? value, bool expected)
    {
        var attribute = value == null ? "" : $" isLocalWorkspace=\"{value}\"";
        var xml = $"""
            <VersionControlServer><Servers><ServerInfo uri="http://tfs.example.test/tfs/alpha">
              <WorkspaceInfo name="ws" computer="DEVBOX"{attribute}>
                <MappedPaths><MappedPath path="C:\Src" /></MappedPaths>
              </WorkspaceInfo>
            </ServerInfo></Servers></VersionControlServer>
            """;

        Assert.AreEqual(expected, TfvcWorkspaceCache.Parse(xml).Single().IsLocalWorkspace);
    }

    [TestMethod]
    [DataRow("<SomethingElse><Servers /></SomethingElse>")]
    [DataRow("<VersionControlServer />")]
    public void Parse_UnexpectedStructure_ReturnsEmpty(string xml) =>
        Assert.IsEmpty(TfvcWorkspaceCache.Parse(xml));

    // TfvcCacheReader relies on this to retry a file caught in the middle of being rewritten.
    [TestMethod]
    public void Parse_TruncatedXml_Throws() =>
        Assert.ThrowsExactly<XmlException>(() => TfvcWorkspaceCache.Parse("<VersionControlServer><Servers>"));
}
