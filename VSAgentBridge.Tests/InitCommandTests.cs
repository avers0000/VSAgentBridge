using VSAgentBridge.Init;

namespace VSAgentBridge.Tests;

// InitCommand: wrong options stop before anything else (no agent CLI is needed for these).
[TestClass]
internal sealed class InitCommandTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("vscode")]
    [DataRow("opencode|--no-marketplace")]
    [DataRow("claude|--roots")]
    public void WrongOptions_UsageAndExitCode2(string argsText)
    {
        var args = argsText.Length == 0 ? Array.Empty<string>() : argsText.Split('|');
        var output = new StringWriter();
        var prompt = new ConsolePrompt(new StringReader(""), output);
        var install = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));

        Assert.AreEqual(2, InitCommand.Run(args, prompt, install));
        StringAssert.Contains(output.ToString(), "Usage:");
        StringAssert.Contains(output.ToString(), "init claude");
        Assert.IsFalse(Directory.Exists(install));
    }
}
