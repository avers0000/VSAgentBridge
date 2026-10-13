using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// PathUtil.SamePath: comparing paths written in agent settings files (settings.json, opencode.json).
[TestClass]
internal sealed class PathUtilSamePathTests
{
    [TestMethod]
    [DataRow(@"D:\Tools\VSAgentBridge", @"D:\Tools\VSAgentBridge")]
    [DataRow(@"D:\Tools\VSAgentBridge", @"d:\tools\vsagentbridge")]
    [DataRow(@"D:\Tools\VSAgentBridge", @"D:\Tools\VSAgentBridge\")]
    [DataRow(@"D:\Tools\VSAgentBridge", "D:/Tools/VSAgentBridge")]
    [DataRow(@"D:\Tools\VSAgentBridge", "D:/Tools/VSAgentBridge/")]
    [DataRow(@"D:\Tools\VSAgentBridge", @" D:\Tools\VSAgentBridge ")]
    [DataRow(@"\\server\share\x", "//server/share/x/")]
    [DataRow("", "")]
    public void Same_True(string a, string b)
    {
        Assert.IsTrue(PathUtil.SamePath(a, b));
        Assert.IsTrue(PathUtil.SamePath(b, a));
    }

    [TestMethod]
    [DataRow(@"D:\Tools\VSAgentBridge", @"D:\Tools\VSAgentBridge2")]
    [DataRow(@"D:\Tools\VSAgentBridge", @"D:\Tools")]
    [DataRow(@"D:\Tools\VSAgentBridge", @"E:\Tools\VSAgentBridge")]
    [DataRow(@"D:\Tools\VSAgentBridge", @"D:\Tools\.\VSAgentBridge")]
    [DataRow(@"D:\My Tools", @"D:\MyTools")]
    public void Different_False(string a, string b)
    {
        Assert.IsFalse(PathUtil.SamePath(a, b));
        Assert.IsFalse(PathUtil.SamePath(b, a));
    }
}
