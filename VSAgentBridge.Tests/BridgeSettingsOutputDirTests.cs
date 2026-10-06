using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class BridgeSettingsOutputDirTests
{
    [TestMethod]
    public void Default_NoOutputDir_ResolvesNextToExe()
    {
        Assert.IsNull(BridgeSettings.Default.OutputDir);
        Assert.AreEqual(@"D:\Tools\VSAB\Output", BridgeSettings.Default.ResolveOutputDir(@"D:\Tools\VSAB"));
    }

    [TestMethod]
    [DataRow(@"D:\\VSAB\\Out", @"D:\VSAB\Out", DisplayName = "drive path")]
    [DataRow(@"D:/VSAB/Out", @"D:\VSAB\Out", DisplayName = "forward slashes")]
    [DataRow(@"D:\\VSAB\\..\\Out", @"D:\Out", DisplayName = "normalized")]
    [DataRow(@"\\\\server\\share\\Out", @"\\server\share\Out", DisplayName = "UNC")]
    public void Parse_AbsoluteOutputDir_Accepted(string jsonValue, string expected)
    {
        var settings = BridgeSettings.Parse($$"""{"outputDir":"{{jsonValue}}"}""", out var warning);

        Assert.IsNull(warning, warning);
        Assert.AreEqual(expected, settings.OutputDir);
        Assert.AreEqual(expected, settings.ResolveOutputDir(@"D:\Tools\VSAB"));
    }

    [TestMethod]
    [DataRow(@"""Output""", DisplayName = "relative")]
    [DataRow(@"""D:Output""", DisplayName = "drive-relative")]
    [DataRow(@"""\\Output""", DisplayName = "root of current drive")]
    [DataRow(@"""""", DisplayName = "empty")]
    [DataRow(@"""   """, DisplayName = "blank")]
    [DataRow("42", DisplayName = "number")]
    [DataRow("null", DisplayName = "null")]
    public void Parse_InvalidOutputDir_DefaultWithWarning(string jsonValue)
    {
        var settings = BridgeSettings.Parse($$"""{"outputDir":{{jsonValue}}, "logLevel":"all"}""", out var warning);

        Assert.IsNull(settings.OutputDir);
        Assert.IsNotNull(warning);
        Assert.IsTrue(warning.Contains("'outputDir'"), warning);
        Assert.AreEqual(LogVerbosity.All, settings.LogLevel, "Other settings must still apply");
    }
}
