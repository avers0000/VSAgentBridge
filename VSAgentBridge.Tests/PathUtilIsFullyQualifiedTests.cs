using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// PathUtil.IsFullyQualified: strict "absolute path" check for workspace roots (init) and outputDir.
[TestClass]
internal sealed class PathUtilIsFullyQualifiedTests
{
    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow(@"D:\Projects")]
    [DataRow(@"d:\projects\app")]
    [DataRow(@"D:/Projects")]
    [DataRow(@"\\server\share")]
    [DataRow(@"\\server\share\folder")]
    [DataRow(@"//server/share")]
    [DataRow(@"D:\My Projects")]
    public void Absolute_True(string path)
    {
        Assert.IsTrue(PathUtil.IsFullyQualified(path));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("D:")]
    [DataRow("D:folder")]
    [DataRow(@"\folder")]
    [DataRow("/folder")]
    [DataRow("folder")]
    [DataRow(@"folder\sub")]
    [DataRow(@".\folder")]
    [DataRow(@"..\folder")]
    [DataRow(@"1:\folder")]
    [DataRow(@" D:\Projects")]
    public void NotAbsolute_False(string path)
    {
        Assert.IsFalse(PathUtil.IsFullyQualified(path));
    }
}
