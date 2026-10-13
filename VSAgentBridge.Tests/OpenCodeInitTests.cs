using System.Text;
using VSAgentBridge.Init;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// OpenCodeInit.Run on temporary folders (without global rules, so the user's real AGENTS.md is never touched).
[TestClass]
internal sealed class OpenCodeInitTests
{
    private string _root = "";
    private string _install = "";
    private string _output = "";
    private string _rootA = "";
    private string _rootB = "";

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "Install");
        _output = Path.Combine(_install, "Output");
        _rootA = Path.Combine(_root, "Work A");
        _rootB = Path.Combine(_root, "Work B");
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_rootA);
        Directory.CreateDirectory(_rootB);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private (int ExitCode, string Output) Run(string input)
    {
        var output = new StringWriter();
        var prompt = new ConsolePrompt(new StringReader(input), output);
        var code = OpenCodeInit.Run(prompt, _install, _output, new[] { _rootA, _rootB }, globalRules: false);
        return (code, output.ToString());
    }

    private string ConfigA => Path.Combine(_rootA, "opencode.json");
    private string ConfigB => Path.Combine(_rootB, "opencode.json");
    private string SettingsFolder => Path.Combine(_install, "Settings");

    [TestMethod]
    public void Declined_NothingChanged()
    {
        var (code, output) = Run("n\n");
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "Nothing changed.");
        StringAssert.Contains(output, ConfigA + ": create it with the bridge's entries");
        Assert.IsFalse(File.Exists(ConfigA));
        Assert.IsFalse(File.Exists(ConfigB));
        Assert.IsFalse(Directory.Exists(SettingsFolder));
        Assert.IsFalse(Directory.Exists(_output));
    }

    [TestMethod]
    public void EndOfInput_NothingChanged()
    {
        var (code, output) = Run("");
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "Nothing changed.");
        Assert.IsFalse(File.Exists(ConfigA));
        Assert.IsFalse(Directory.Exists(SettingsFolder));
    }

    [TestMethod]
    public void Applied_FilesWrittenCopiesKept()
    {
        File.WriteAllText(ConfigB, "{\n  \"model\": \"ollama/gpt-oss\"\n}\n", new UTF8Encoding(false));

        var (code, output) = Run("y\n");
        Assert.AreEqual(0, code, output);
        StringAssert.Contains(output, "Done.");
        StringAssert.Contains(output, ConfigB + ": add the bridge's entries");
        StringAssert.Contains(output, "opencode service restart");
        StringAssert.Contains(output, "AGENTS.sample.md");
        Assert.IsTrue(Directory.Exists(_output));

        foreach (var config in new[] { ConfigA, ConfigB })
        {
            Assert.IsTrue(JsonFile.TryParse(File.ReadAllText(config), out var file, out var error), error);
            Assert.AreEqual(Path.Combine(_install, "opencode"), file!.Root["plugins"]![0]!.GetValue<string>());
            Assert.AreEqual(Path.Combine(_install, "vsagentbridge.exe"),
                file.Root["mcp"]!["servers"]!["vsab"]!["command"]![0]!.GetValue<string>());
        }
        StringAssert.StartsWith(File.ReadAllText(ConfigB), "{\n  \"model\": \"ollama/gpt-oss\",\n");

        var runFolder = Directory.GetDirectories(Path.Combine(SettingsFolder, "opencode"))
            .Single(d => Path.GetFileName(d) != "init");
        StringAssert.Contains(output, runFolder);
        Assert.IsTrue(File.Exists(Path.Combine(runFolder, "before", SettingsBackup.MirrorPath(ConfigB))));
        Assert.IsFalse(File.Exists(Path.Combine(runFolder, "before", SettingsBackup.MirrorPath(ConfigA))));
        Assert.IsTrue(File.Exists(Path.Combine(runFolder, "after", SettingsBackup.MirrorPath(ConfigA))));
        Assert.IsTrue(File.Exists(Path.Combine(SettingsFolder, "opencode", "init", SettingsBackup.MirrorPath(ConfigB))));
    }

    [TestMethod]
    public void SecondRun_UpToDate_NoQuestionNoRunFolder()
    {
        Run("y\n");
        var before = File.ReadAllText(ConfigA);
        var runFolders = Directory.GetDirectories(Path.Combine(SettingsFolder, "opencode")).Length;

        var (code, output) = Run("");
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, ConfigA + ": the bridge's entries are up to date");
        StringAssert.Contains(output, ConfigB + ": the bridge's entries are up to date");
        StringAssert.Contains(output, "Everything is up to date; nothing to change.");
        Assert.IsFalse(output.Contains("Apply?"));
        Assert.IsFalse(output.Contains("opencode service restart"));
        Assert.AreEqual(before, File.ReadAllText(ConfigA));
        Assert.AreEqual(runFolders, Directory.GetDirectories(Path.Combine(SettingsFolder, "opencode")).Length);
    }

    [TestMethod]
    public void UpToDate_ButOutputMissing_StillAsks()
    {
        Run("y\n");
        Directory.Delete(_output);

        var (code, output) = Run("y\n");
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "Create the folder " + _output);
        StringAssert.Contains(output, "Apply?");
        Assert.IsTrue(Directory.Exists(_output));
    }

    [TestMethod]
    public void BrokenJson_StopsBeforeAsking_NothingChanged()
    {
        File.WriteAllText(ConfigB, "{ \"model\": ", new UTF8Encoding(false));
        var (code, output) = Run("y\n");
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, ConfigB + ": invalid JSON");
        Assert.IsFalse(output.Contains("Apply?"));
        Assert.IsFalse(File.Exists(ConfigA));
        Assert.IsFalse(Directory.Exists(SettingsFolder));
    }

    [TestMethod]
    public void UnexpectedStructure_StopsBeforeAsking()
    {
        File.WriteAllText(ConfigA, "{ \"permission\": { \"external_directory\": \"ask\" } }", new UTF8Encoding(false));
        var (code, output) = Run("y\n");
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, "by hand");
        Assert.IsFalse(output.Contains("Apply?"));
        Assert.IsFalse(Directory.Exists(SettingsFolder));
    }

    [TestMethod]
    public void CommentsAndJsonc_Warned()
    {
        File.WriteAllText(ConfigA, "{\n  // mine\n  \"model\": \"x\"\n}\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_rootB, "opencode.jsonc"), "{}", new UTF8Encoding(false));
        var (_, output) = Run("n\n");
        StringAssert.Contains(output, "WARNING: " + ConfigA + " has comments");
        StringAssert.Contains(output, "WARNING: " + _rootB + " also has opencode.jsonc");
    }

    [TestMethod]
    public void ReadOnlyConfig_ErrorAndCopiesShown()
    {
        File.WriteAllText(ConfigB, "{}", new UTF8Encoding(false));
        File.SetAttributes(ConfigB, FileAttributes.ReadOnly);
        try
        {
            var (code, output) = Run("y\n");
            Assert.AreEqual(1, code);
            StringAssert.Contains(output, "ERROR:");
            StringAssert.Contains(output, "Copies of the changed files");
            Assert.AreEqual("{}", File.ReadAllText(ConfigB));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
