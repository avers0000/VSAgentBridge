using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class VsInstanceLabelTests
{
    [TestMethod]
    public void Format_VersionAndPid()
    {
        Assert.AreEqual("17.0/pid12345", VsInstanceLabel.Format("17.0", 12345));
    }

    [TestMethod]
    [DataRow("16.0", "VS2019")]
    [DataRow("17.0", "VS2022")]
    [DataRow("18.0", "VS2026")]
    [DataRow("19.0", "VS19.0")]
    public void Describe_ProductYearWithoutSpace(string version, string product)
    {
        Assert.AreEqual($"{product} (pid 42)", VsInstanceLabel.Describe(VsInstanceLabel.Format(version, 42)));
    }

    [TestMethod]
    public void Describe_WithSolution_AddsFileNameOnly()
    {
        var text = VsInstanceLabel.Describe("16.0/pid16616", @"D:\Src\Tools\SampleApp.sln");

        Assert.AreEqual("VS2019 (pid 16616): SampleApp.sln", text);
    }

    [TestMethod]
    public void Describe_UnexpectedLabel_ReturnedAsIs()
    {
        Assert.AreEqual("something", VsInstanceLabel.Describe("something"));
    }
}
