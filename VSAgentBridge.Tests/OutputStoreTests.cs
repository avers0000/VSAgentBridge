using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class OutputStoreTests
{
    private string _root = "";
    private string _sessionFolder = "";
    private string _source = "";
    private OutputStore _store = null!;

    [TestInitialize]
    public void CreateFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        _sessionFolder = Path.Combine(_root, "Output", "ses_1");
        _source = Path.Combine(_root, "Src");
        Directory.CreateDirectory(_source);
        _store = new OutputStore(_sessionFolder, "ses_1", "opencode", @"D:\Work\Кириллица");
    }

    [TestCleanup]
    public void DeleteFolders()
    {
        // Copies of read-only originals and read-only test files must be made writable before deleting.
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    // ---- PrepareCopy ----

    [TestMethod]
    public void PrepareCopy_NoCopyYet_CopiesBytesAndClearsReadOnly()
    {
        // BOM + CRLF + tab + Cyrillic: the copy must be byte for byte.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("a\r\n\tб\r\n")).ToArray();
        var original = Source("X.cs", bytes, readOnly: true);
        var target = Target(@"App\X.cs");

        var result = _store.PrepareCopy(original, target);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Copied);
        Assert.IsFalse(result.Reused);
        Assert.AreEqual("new", result.CopyState);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(target));
        Assert.AreEqual((FileAttributes)0, File.GetAttributes(target) & FileAttributes.ReadOnly, "Copy must be writable");
        Assert.AreNotEqual((FileAttributes)0, File.GetAttributes(original) & FileAttributes.ReadOnly, "Original must stay read-only");
    }

    [TestMethod]
    public void PrepareCopy_CopyExists_ReusedUntouched()
    {
        var original = Source("X.cs", "original");
        var target = Target(@"App\X.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "edited in this session");

        var result = _store.PrepareCopy(original, target);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsFalse(result.Copied);
        Assert.IsTrue(result.Reused);
        Assert.AreEqual("reused", result.CopyState);
        Assert.AreEqual("edited in this session", File.ReadAllText(target));
    }

    [TestMethod]
    public void PrepareCopy_MissingOriginal_Failed()
    {
        var result = _store.PrepareCopy(Path.Combine(_source, "Missing.cs"), Target("Missing.cs"));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Error!.Contains("FileNotFoundException"), result.Error);
    }

    [TestMethod]
    public void PrepareCopy_FolderBlockedByAFile_Failed()
    {
        // A file where the target's folder must be: CreateDirectory fails with an IOException.
        Directory.CreateDirectory(_sessionFolder);
        File.WriteAllText(Path.Combine(_sessionFolder, "App"), "not a folder");

        var result = _store.PrepareCopy(Source("X.cs", "x"), Target(@"App\X.cs"));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Copied);
        Assert.IsNotNull(result.Error);
    }

    // ---- PrepareNewFile ----

    [TestMethod]
    public void PrepareNewFile_CreatesFoldersOnly()
    {
        var target = Target(@"App\New\Deeper\New.cs");

        var result = _store.PrepareNewFile(target);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(target)));
        Assert.IsFalse(File.Exists(target));
        Assert.AreEqual("none", result.CopyState);
    }

    [TestMethod]
    public void PrepareNewFile_FileAlreadyThere_ReusedUntouched()
    {
        var target = Target("New.cs");
        Directory.CreateDirectory(_sessionFolder);
        File.WriteAllText(target, "created earlier in this session");

        var result = _store.PrepareNewFile(target);

        Assert.IsTrue(result.Reused);
        Assert.AreEqual("created earlier in this session", File.ReadAllText(target));
    }

    // ---- CopyCreatedFile ----

    [TestMethod]
    public void CopyCreatedFile_NoCopyYet_Copies()
    {
        var created = Source("New.cs", "new content");
        var target = Target(@"App\New.cs");

        var result = _store.CopyCreatedFile(created, target);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Copied);
        Assert.IsFalse(result.Reused);
        Assert.AreEqual("new content", File.ReadAllText(target));
        Assert.IsTrue(File.Exists(created), "The created file stays in place");
    }

    [TestMethod]
    public void CopyCreatedFile_ReadOnlyCopyExists_Replaced()
    {
        var created = Source("New.cs", "just written");
        var target = Target("New.cs");
        Directory.CreateDirectory(_sessionFolder);
        File.WriteAllText(target, "older");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var result = _store.CopyCreatedFile(created, target);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(result.Copied);
        Assert.IsTrue(result.Reused);
        Assert.AreEqual("just written", File.ReadAllText(target));
        Assert.AreEqual((FileAttributes)0, File.GetAttributes(target) & FileAttributes.ReadOnly);
    }

    // ---- Session info file ----

    [TestMethod]
    public void FirstRedirect_WritesSessionInfo()
    {
        _store.PrepareNewFile(Target("New.cs"));

        var info = File.ReadAllText(InfoPath(), Encoding.UTF8);
        Assert.IsTrue(info.Contains("session: ses_1"), info);
        Assert.IsTrue(info.Contains("harness: opencode"), info);
        Assert.IsTrue(info.Contains(@"cwd: D:\Work\Кириллица"), info);
        Assert.IsTrue(info.Contains("by comparing it with the original"), info);

        var bytes = File.ReadAllBytes(InfoPath());
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "UTF-8 with BOM");
    }

    [TestMethod]
    public void LaterRedirects_DoNotRewriteSessionInfo()
    {
        _store.PrepareNewFile(Target("A.cs"));
        File.WriteAllText(InfoPath(), "kept");

        _store.PrepareNewFile(Target("B.cs"));
        new OutputStore(_sessionFolder, "ses_1", "opencode", null).PrepareNewFile(Target("C.cs"));

        Assert.AreEqual("kept", File.ReadAllText(InfoPath()));
    }

    [TestMethod]
    public void NoSession_InfoSaysNone()
    {
        var store = new OutputStore(_sessionFolder, session: null, harness: null, cwd: null);

        store.PrepareNewFile(Target("A.cs"));

        var info = File.ReadAllText(InfoPath());
        Assert.IsTrue(info.Contains("session: (none)"), info);
        Assert.IsTrue(info.Contains("cwd: (unknown)"), info);
    }

    // ---- Helpers ----

    private string Target(string relative) => Path.Combine(_sessionFolder, relative);

    private string InfoPath() => Path.Combine(_sessionFolder, OutputLayout.SessionInfoFileName);

    private string Source(string name, string text, bool readOnly = false) =>
        Source(name, Encoding.UTF8.GetBytes(text), readOnly);

    private string Source(string name, byte[] bytes, bool readOnly = false)
    {
        var path = Path.Combine(_source, name);
        File.WriteAllBytes(path, bytes);
        if (readOnly)
            File.SetAttributes(path, FileAttributes.ReadOnly);
        return path;
    }
}
