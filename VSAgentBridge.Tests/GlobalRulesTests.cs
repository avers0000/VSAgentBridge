using System.Text;
using VSAgentBridge.Init;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// GlobalRules: preparing (nothing changed) and applying the rules block in a global rules file.
[TestClass]
internal sealed class GlobalRulesTests
{
    private static readonly string[] Roots = { @"D:\Projects", @"E:\Work" };
    private static readonly DateTime Now = new(2026, 10, 13, 22, 50, 0);

    private string _root = "";
    private string _install = "";
    private string _rulesPath = "";

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "Install");
        Directory.CreateDirectory(_install);
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RulesBlock.SampleFileName),
            Path.Combine(_install, RulesBlock.SampleFileName));
        _rulesPath = Path.Combine(_root, "User", ".claude", "CLAUDE.md");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private GlobalRules Prepare()
    {
        Assert.IsTrue(GlobalRules.TryPrepare(_install, _rulesPath, Roots, out var rules, out var error), error);
        return rules!;
    }

    private void WriteRules(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rulesPath)!);
        File.WriteAllText(_rulesPath, text, new UTF8Encoding(false));
    }

    private SettingsBackup Backup() => SettingsBackup.Start(_install, "claude", Now);

    [TestMethod]
    public void NoSample_Error()
    {
        File.Delete(Path.Combine(_install, RulesBlock.SampleFileName));
        Assert.IsFalse(GlobalRules.TryPrepare(_install, _rulesPath, Roots, out var rules, out var error));
        Assert.IsNull(rules);
        StringAssert.Contains(error, "is missing");
    }

    [TestMethod]
    public void BrokenSample_Error()
    {
        File.WriteAllText(Path.Combine(_install, RulesBlock.SampleFileName), "# no markers\n");
        Assert.IsFalse(GlobalRules.TryPrepare(_install, _rulesPath, Roots, out _, out var error));
        StringAssert.Contains(error, "markers");
    }

    [TestMethod]
    public void NewFile_Created()
    {
        var rules = Prepare();
        Assert.AreEqual(RulesBlockChange.Appended, rules.Change);
        Assert.IsTrue(rules.WillWrite);
        StringAssert.Contains(rules.Describe(), "create it");
        Assert.IsFalse(File.Exists(_rulesPath), "prepare must not change anything");

        var backup = Backup();
        Assert.IsNull(rules.Apply(backup));
        var text = File.ReadAllText(_rulesPath);
        StringAssert.StartsWith(text, RulesBlock.BeginMarker + "\r\n");
        StringAssert.Contains(text, "\r\n  - D:\\Projects\r\n  - E:\\Work\r\n");
        Assert.IsFalse(File.Exists(backup.BeforePath(_rulesPath)));
        Assert.IsTrue(File.Exists(backup.AfterPath(_rulesPath)));
    }

    [TestMethod]
    public void UserFile_AppendedWithBeforeCopy()
    {
        WriteRules("# Mine\nBe brief.\n");
        var rules = Prepare();
        StringAssert.Contains(rules.Describe(), "add the bridge's rules at the end");

        var backup = Backup();
        rules.Apply(backup);
        StringAssert.StartsWith(File.ReadAllText(_rulesPath), "# Mine\nBe brief.\n\n" + RulesBlock.BeginMarker + "\n");
        Assert.AreEqual("# Mine\nBe brief.\n", File.ReadAllText(backup.BeforePath(_rulesPath)));
    }

    [TestMethod]
    public void SecondTime_UpToDate_NotWritten()
    {
        Prepare().Apply(Backup());
        var written = File.ReadAllText(_rulesPath);

        var again = Prepare();
        Assert.AreEqual(RulesBlockChange.Unchanged, again.Change);
        Assert.IsFalse(again.WillWrite);
        StringAssert.Contains(again.Describe(), "up to date");

        var backup = SettingsBackup.Start(_install, "claude", Now.AddMinutes(1));
        Assert.IsNull(again.Apply(backup));
        Assert.AreEqual(written, File.ReadAllText(_rulesPath));
        Assert.IsFalse(File.Exists(backup.BeforePath(_rulesPath)));
    }

    [TestMethod]
    public void OtherRoots_Replaced()
    {
        Prepare().Apply(Backup());
        Assert.IsTrue(GlobalRules.TryPrepare(_install, _rulesPath, new[] { @"F:\Other" }, out var rules, out _));
        Assert.AreEqual(RulesBlockChange.Replaced, rules!.Change);
        StringAssert.Contains(rules.Describe(), "update the bridge's rules");

        rules.Apply(SettingsBackup.Start(_install, "claude", Now.AddMinutes(1)));
        var text = File.ReadAllText(_rulesPath);
        StringAssert.Contains(text, @"  - F:\Other");
        Assert.IsFalse(text.Contains(@"D:\Projects"));
    }

    [TestMethod]
    public void Ambiguous_FileKept_BlockSavedForManualCopy()
    {
        var original = "# Mine\n" + RulesBlock.BeginMarker + "\nold\n";
        WriteRules(original);
        var rules = Prepare();
        Assert.AreEqual(RulesBlockChange.Ambiguous, rules.Change);
        Assert.IsFalse(rules.WillWrite);
        StringAssert.Contains(rules.Describe(), "WARNING");

        var backup = Backup();
        var manual = rules.Apply(backup);
        Assert.AreEqual(Path.Combine(backup.RunFolder, GlobalRules.ManualCopyFileName), manual);
        Assert.AreEqual(original, File.ReadAllText(_rulesPath));

        var block = File.ReadAllText(manual!);
        StringAssert.StartsWith(block, RulesBlock.BeginMarker + "\r\n");
        StringAssert.EndsWith(block, RulesBlock.EndMarker + "\r\n");
        StringAssert.Contains(block, "  - E:\\Work\r\n");
    }
}
