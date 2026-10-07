using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class OutputStorePendingOperationsTests
{
    private string _root = "";
    private string _sessionFolder = "";
    private OutputStore _store = null!;

    [TestInitialize]
    public void CreateFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        _sessionFolder = Path.Combine(_root, "Output", "ses_1");
        _store = new OutputStore(_sessionFolder, "ses_1", "opencode", @"D:\Work");
    }

    [TestCleanup]
    public void DeleteFolders()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Path_IsInTheSessionFolder()
    {
        Assert.AreEqual(Path.Combine(_sessionFolder, "_pending-operations.txt"), _store.PendingOperationsPath);
    }

    [TestMethod]
    public void First_CreatesSessionFolderInfoAndRecord_WithBom()
    {
        var error = _store.RecordPendingOperation("shell", "Remove-Item .\\Old.cs", new[] { "deletes files: Remove-Item .\\Old.cs" });

        Assert.IsNull(error, error);
        Assert.IsTrue(File.Exists(Path.Combine(_sessionFolder, OutputLayout.SessionInfoFileName)));

        var bytes = File.ReadAllBytes(_store.PendingOperationsPath);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "UTF-8 BOM");

        var lines = File.ReadAllLines(_store.PendingOperationsPath, Encoding.UTF8);
        StringAssertStartsWithDate(lines[0]);
        Assert.IsTrue(lines[0].EndsWith("  opencode  shell", StringComparison.Ordinal), lines[0]);
        Assert.AreEqual("  > Remove-Item .\\Old.cs", lines[1]);
        Assert.AreEqual("  deletes files: Remove-Item .\\Old.cs", lines[2]);
        Assert.AreEqual("", lines[3]);
    }

    [TestMethod]
    public void Second_AppendsWithoutAnotherBom()
    {
        _store.RecordPendingOperation("shell", "del a.cs", new[] { "one" });
        _store.RecordPendingOperation("patch", "*** Delete File: b.cs", new[] { "two" });

        var bytes = File.ReadAllBytes(_store.PendingOperationsPath);
        Assert.AreEqual(1, CountBoms(bytes));

        var text = File.ReadAllText(_store.PendingOperationsPath, Encoding.UTF8);
        Assert.IsTrue(text.Contains("  > del a.cs"), text);
        Assert.IsTrue(text.Contains("  > *** Delete File: b.cs"), text);
        Assert.IsTrue(text.IndexOf("one", StringComparison.Ordinal) < text.IndexOf("two", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public void MultiLineCommand_EachLineMarked_CyrillicKept()
    {
        _store.RecordPendingOperation("shell", "cd Папка\r\nrm x.cs\n", Array.Empty<string>());

        var lines = File.ReadAllLines(_store.PendingOperationsPath, Encoding.UTF8);
        Assert.AreEqual("  > cd Папка", lines[1]);
        Assert.AreEqual("  > rm x.cs", lines[2]);
        Assert.AreEqual("", lines[3]);
    }

    [TestMethod]
    public void LongCommand_IsCut()
    {
        _store.RecordPendingOperation("shell", "rm " + new string('x', 5000), Array.Empty<string>());

        var line = File.ReadAllLines(_store.PendingOperationsPath, Encoding.UTF8)[1];
        Assert.IsTrue(line.EndsWith(" [...]", StringComparison.Ordinal), line);
        Assert.IsTrue(line.Length < 2100, line.Length.ToString());
    }

    [TestMethod]
    public void FolderCannotBeCreated_ReturnsError_DoesNotThrow()
    {
        // The session folder's parent is a file: the folder cannot be created.
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "Output");
        File.WriteAllText(blocker, "not a folder");

        var error = _store.RecordPendingOperation("shell", "rm x", Array.Empty<string>());

        Assert.IsNotNull(error);
        Assert.IsTrue(error!.Contains("Exception"), error);
    }

    [TestMethod]
    public void ParallelAppends_AllKept()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => new OutputStore(_sessionFolder, "ses_1", "opencode", null)
                .RecordPendingOperation("shell", $"rm file{i}.cs", new[] { $"marker-{i}" })))
            .ToArray();
        Task.WaitAll(tasks);

        Assert.IsTrue(tasks.All(t => t.Result == null), string.Join("; ", tasks.Select(t => t.Result)));
        var text = File.ReadAllText(_store.PendingOperationsPath, Encoding.UTF8);
        for (var i = 0; i < 10; i++)
            Assert.IsTrue(text.Contains($"  marker-{i}"), text);
    }

    private static void StringAssertStartsWithDate(string line) =>
        Assert.IsTrue(DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _), line);

    private static int CountBoms(byte[] bytes)
    {
        var count = 0;
        for (var i = 0; i + 2 < bytes.Length; i++)
            if (bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF) count++;
        return count;
    }
}
