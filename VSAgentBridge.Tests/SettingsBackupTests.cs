using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// SettingsBackup: copies under <install>\Settings\<harness>\ and writing through after\.
[TestClass]
internal sealed class SettingsBackupTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 22, 15, 0);
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    private string _root = "";
    private string _install = "";
    private string _user = "";

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "Install");
        _user = Path.Combine(_root, "User");
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_user);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private string HarnessFolder => Path.Combine(_install, "Settings", "claude");

    private string UserFile(string name, string? content)
    {
        var path = Path.Combine(_user, name);
        if (content != null)
            File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // ---- MirrorPath ----

    [TestMethod]
    [DataRow(@"C:\Users\x\.claude\settings.json", @"C\Users\x\.claude\settings.json")]
    [DataRow(@"D:\Work\opencode.json", @"D\Work\opencode.json")]
    [DataRow("D:/Work/opencode.json", @"D\Work\opencode.json")]
    [DataRow(@"\\server\share\Work\opencode.json", @"UNC\server\share\Work\opencode.json")]
    public void MirrorPath(string path, string expected)
    {
        Assert.AreEqual(expected, SettingsBackup.MirrorPath(path));
    }

    // ---- Start ----

    [TestMethod]
    public void Start_First_RunAndInitFoldersCreated()
    {
        var backup = SettingsBackup.Start(_install, "claude", Now);
        Assert.IsTrue(backup.IsFirstRun);
        Assert.AreEqual(Path.Combine(HarnessFolder, "20261013-221500"), backup.RunFolder);
        Assert.AreEqual(Path.Combine(HarnessFolder, "init"), backup.InitFolder);
        Assert.IsTrue(Directory.Exists(backup.RunFolder));
        Assert.IsTrue(Directory.Exists(backup.InitFolder));
    }

    [TestMethod]
    public void Start_Second_NotFirst()
    {
        SettingsBackup.Start(_install, "claude", Now);
        var second = SettingsBackup.Start(_install, "claude", Now.AddMinutes(1));
        Assert.IsFalse(second.IsFirstRun);
        Assert.AreEqual(Path.Combine(HarnessFolder, "20261013-221600"), second.RunFolder);
    }

    [TestMethod]
    public void Start_SameSecond_SuffixAdded()
    {
        var first = SettingsBackup.Start(_install, "claude", Now);
        var second = SettingsBackup.Start(_install, "claude", Now);
        var third = SettingsBackup.Start(_install, "claude", Now);
        Assert.AreEqual(Path.Combine(HarnessFolder, "20261013-221500"), first.RunFolder);
        Assert.AreEqual(Path.Combine(HarnessFolder, "20261013-221500-2"), second.RunFolder);
        Assert.AreEqual(Path.Combine(HarnessFolder, "20261013-221500-3"), third.RunFolder);
    }

    [TestMethod]
    public void Start_HarnessesSeparate()
    {
        SettingsBackup.Start(_install, "claude", Now);
        var openCode = SettingsBackup.Start(_install, "opencode", Now);
        Assert.IsTrue(openCode.IsFirstRun);
        StringAssert.StartsWith(openCode.RunFolder, Path.Combine(_install, "Settings", "opencode"));
    }

    // ---- ReadIfExists ----

    [TestMethod]
    public void ReadIfExists_Missing_Null()
    {
        Assert.IsNull(SettingsBackup.ReadIfExists(Path.Combine(_user, "none.json")));
    }

    [TestMethod]
    public void ReadIfExists_BomRemoved()
    {
        var path = Path.Combine(_user, "a.json");
        File.WriteAllBytes(path, Bom.Concat(Encoding.UTF8.GetBytes("{}")).ToArray());
        Assert.AreEqual("{}", SettingsBackup.ReadIfExists(path));
    }

    // ---- SaveBefore ----

    [TestMethod]
    public void SaveBefore_FirstRun_BeforeAndInit()
    {
        var path = UserFile("settings.json", "original");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.SaveBefore(path);

        Assert.AreEqual("original", File.ReadAllText(backup.BeforePath(path)));
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(backup.InitFolder, SettingsBackup.MirrorPath(path))));
        StringAssert.StartsWith(backup.BeforePath(path), Path.Combine(backup.RunFolder, "before"));
    }

    [TestMethod]
    public void SaveBefore_LaterRun_InitKeepsTheOriginal()
    {
        var path = UserFile("settings.json", "original");
        SettingsBackup.Start(_install, "claude", Now).SaveBefore(path);

        File.WriteAllText(path, "changed by the bridge");
        var second = SettingsBackup.Start(_install, "claude", Now.AddMinutes(1));
        second.SaveBefore(path);

        Assert.AreEqual("changed by the bridge", File.ReadAllText(second.BeforePath(path)));
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(second.InitFolder, SettingsBackup.MirrorPath(path))));
    }

    [TestMethod]
    public void SaveBefore_Missing_NothingCopied()
    {
        var path = UserFile("settings.json", null);
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.SaveBefore(path);

        Assert.IsFalse(File.Exists(backup.BeforePath(path)));
        Assert.AreEqual(0, Directory.GetFiles(backup.InitFolder, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void SaveBefore_SameNameDifferentFolders_NoCollision()
    {
        Directory.CreateDirectory(Path.Combine(_user, "A"));
        Directory.CreateDirectory(Path.Combine(_user, "B"));
        var a = UserFile(Path.Combine("A", "opencode.json"), "a");
        var b = UserFile(Path.Combine("B", "opencode.json"), "b");
        var backup = SettingsBackup.Start(_install, "opencode", Now);
        backup.SaveBefore(a);
        backup.SaveBefore(b);

        Assert.AreEqual("a", File.ReadAllText(backup.BeforePath(a)));
        Assert.AreEqual("b", File.ReadAllText(backup.BeforePath(b)));
    }

    // ---- Write ----

    [TestMethod]
    public void Write_Existing_AfterAndOriginalSame_BeforeKept()
    {
        var path = UserFile("settings.json", "old");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.SaveBefore(path);
        backup.Write(path, "new");

        Assert.AreEqual("new", File.ReadAllText(path));
        Assert.AreEqual("new", File.ReadAllText(backup.AfterPath(path)));
        Assert.AreEqual("old", File.ReadAllText(backup.BeforePath(path)));
    }

    [TestMethod]
    public void Write_NewFileInNewFolder_CreatedWithoutBom()
    {
        var path = Path.Combine(_user, "New", "Sub", "AGENTS.md");
        var backup = SettingsBackup.Start(_install, "opencode", Now);
        backup.SaveBefore(path);
        backup.Write(path, "rules");

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("rules"), File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(backup.BeforePath(path)));
    }

    [TestMethod]
    public void Write_OriginalWithBom_BomKept()
    {
        var path = Path.Combine(_user, "CLAUDE.md");
        File.WriteAllBytes(path, Bom.Concat(Encoding.UTF8.GetBytes("old")).ToArray());
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.Write(path, "new");

        CollectionAssert.AreEqual(Bom.Concat(Encoding.UTF8.GetBytes("new")).ToArray(), File.ReadAllBytes(path));
        CollectionAssert.AreEqual(File.ReadAllBytes(path), File.ReadAllBytes(backup.AfterPath(path)));
    }

    [TestMethod]
    public void Write_OriginalWithoutBom_NoBom_NonAsciiKept()
    {
        var path = UserFile("settings.json", "old");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.Write(path, "caf\u00e9");

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("caf\u00e9"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Write_LineBreaksWrittenAsGiven()
    {
        var path = UserFile("settings.json", "old");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.Write(path, "a\nb\r\nc");

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("a\nb\r\nc"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Write_ReadOnlyOriginal_ThrowsAndOriginalKept()
    {
        var path = UserFile("opencode.json", "old");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var backup = SettingsBackup.Start(_install, "opencode", Now);
        backup.SaveBefore(path);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => backup.Write(path, "new"));
        Assert.AreEqual("old", File.ReadAllText(path));
        Assert.IsTrue(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    // ---- SaveAfter ----

    [TestMethod]
    public void SaveAfter_FileChangedByACommand_Copied()
    {
        var path = UserFile("settings.json", "before command");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.SaveBefore(path);
        File.WriteAllText(path, "after command");
        backup.SaveAfter(path);

        Assert.AreEqual("before command", File.ReadAllText(backup.BeforePath(path)));
        Assert.AreEqual("after command", File.ReadAllText(backup.AfterPath(path)));
    }

    [TestMethod]
    public void SaveAfter_AfterWrite_SameContent()
    {
        var path = UserFile("settings.json", "old");
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.Write(path, "new");
        backup.SaveAfter(path);
        Assert.AreEqual("new", File.ReadAllText(backup.AfterPath(path)));
    }

    [TestMethod]
    public void SaveAfter_Missing_NothingCopied()
    {
        var path = UserFile("settings.json", null);
        var backup = SettingsBackup.Start(_install, "claude", Now);
        backup.SaveAfter(path);
        Assert.IsFalse(File.Exists(backup.AfterPath(path)));
    }
}
