using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// HarnessVersion: the "--version" output of the agent CLIs and the minimum versions for init.
[TestClass]
internal sealed class HarnessVersionTests
{
    [TestMethod]
    [DataRow("2.1.292 (Claude Code)", "2.1.292")]
    [DataRow("opencode v2.0.6", "2.0.6")]
    [DataRow("opencode V2.0.6", "2.0.6")]
    [DataRow("v2.0.6", "2.0.6")]
    [DataRow("2.0", "2.0.0")]
    [DataRow("2.1.292-beta.1", "2.1.292")]
    [DataRow("\r\n  2.1.292\r\n", "2.1.292")]
    [DataRow("version 10.20.30", "10.20.30")]
    [DataRow("12.3.4.5", "12.3.4")]
    [DataRow("tool 1.2.3 (runtime 4.5.6)", "1.2.3")]
    public void Parse_Version(string output, string expected)
    {
        Assert.AreEqual(Version.Parse(expected), HarnessVersion.Parse(output));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("no version here")]
    [DataRow("2")]
    [DataRow("v.")]
    [DataRow("99999999999.1.0")]
    [DataRow("2.99999999999")]
    [DataRow("2.1.99999999999")]
    public void Parse_NoVersion_Null(string? output)
    {
        Assert.IsNull(HarnessVersion.Parse(output));
    }

    [TestMethod]
    public void Minimums()
    {
        Assert.AreEqual(new Version(2, 1, 287), HarnessVersion.ClaudeMinimum);
        Assert.AreEqual(new Version(2, 0, 0), HarnessVersion.OpenCodeMinimum);
    }

    [TestMethod]
    [DataRow("2.1.287 (Claude Code)")]
    [DataRow("2.1.292 (Claude Code)")]
    [DataRow("2.2.0 (Claude Code)")]
    [DataRow("3.0 (Claude Code)")]
    public void Claude_AtLeast_True(string output)
    {
        Assert.IsTrue(HarnessVersion.IsAtLeast(output, HarnessVersion.ClaudeMinimum, out var version));
        Assert.IsNotNull(version);
    }

    [TestMethod]
    [DataRow("2.1.286 (Claude Code)")]
    [DataRow("2.1 (Claude Code)")]
    [DataRow("2.0.999 (Claude Code)")]
    [DataRow("1.9.9 (Claude Code)")]
    public void Claude_Older_False(string output)
    {
        Assert.IsFalse(HarnessVersion.IsAtLeast(output, HarnessVersion.ClaudeMinimum, out var version));
        Assert.IsNotNull(version);
    }

    [TestMethod]
    [DataRow("opencode v2.0.6")]
    [DataRow("opencode v2.0")]
    [DataRow("opencode v2.0.0")]
    [DataRow("opencode v3.1.0")]
    public void OpenCode_AtLeast_True(string output)
    {
        Assert.IsTrue(HarnessVersion.IsAtLeast(output, HarnessVersion.OpenCodeMinimum, out _));
    }

    [TestMethod]
    [DataRow("opencode v1.14.2")]
    [DataRow("opencode v0.9")]
    public void OpenCode_Older_False(string output)
    {
        Assert.IsFalse(HarnessVersion.IsAtLeast(output, HarnessVersion.OpenCodeMinimum, out _));
    }

    [TestMethod]
    public void IsAtLeast_NoVersion_FalseAndNull()
    {
        Assert.IsFalse(HarnessVersion.IsAtLeast("command not recognized", HarnessVersion.ClaudeMinimum, out var version));
        Assert.IsNull(version);
    }

    [TestMethod]
    public void IsAtLeast_ReturnsParsedVersion()
    {
        HarnessVersion.IsAtLeast("opencode v2.0.6", HarnessVersion.OpenCodeMinimum, out var version);
        Assert.AreEqual(new Version(2, 0, 6), version);
    }
}
