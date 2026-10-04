using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class TfvcPathClassifierTests
{
    private const string Machine = "DEVBOX";
    private static readonly DateTime Older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = Older.AddHours(1);

    [TestMethod]
    [DataRow(@"C:\Src\Alpha", @"C:\Src\Alpha\Web\Site.cs", true)]
    [DataRow(@"C:\Src\Alpha", @"C:\Src\Alpha", true)]            // the root itself
    [DataRow(@"C:\Src\Alpha", @"c:\src\ALPHA\Site.cs", true)]     // case-insensitive
    [DataRow(@"C:\Src\Alpha\", @"C:\Src\Alpha\Site.cs", true)]    // trailing separator in the cache
    [DataRow(@"D:\", @"D:\Any\Site.cs", true)]                    // whole drive mapped
    [DataRow(@"C:\Src\Alpha", @"C:\Src\AlphaBeta\Site.cs", false)] // same prefix, other folder
    [DataRow(@"C:\Src\Alpha", @"C:\Src\Site.cs", false)]          // parent of the root
    [DataRow(@"C:\Src\Bad|Name", @"C:\Src\Bad|Name\Site.cs", false)] // unusable cache entry is ignored
    public void Classify_FolderBoundary(string mappedPath, string fullPath, bool expectedMapped)
    {
        var cache = Cache(@"C:\cache\a.config", Older, Workspace("ws", paths: mappedPath));

        var result = TfvcPathClassifier.Classify(fullPath, Machine, new[] { cache });

        Assert.AreEqual(expectedMapped ? TfvcPathStatus.ServerWorkspace : TfvcPathStatus.NotMapped, result.Status);
    }

    [TestMethod]
    [DataRow(@"C:\Src\Alpha\Site.cs", "inner", TfvcPathStatus.LocalWorkspace, @"C:\Src\Alpha")]
    [DataRow(@"C:\Src\Other\Site.cs", "outer", TfvcPathStatus.ServerWorkspace, @"C:\Src")]
    public void Classify_LongestRootWins(string fullPath, string expectedWorkspace, TfvcPathStatus expectedStatus, string expectedRoot)
    {
        // The broader mapping comes first: "first match wins" would fail here.
        var cache = Cache(@"C:\cache\a.config", Older,
            Workspace("outer", paths: new[] { @"C:\Elsewhere", @"C:\Src" }),
            Workspace("inner", local: true, paths: @"C:\Src\Alpha"));

        var result = TfvcPathClassifier.Classify(fullPath, Machine, new[] { cache });

        Assert.AreEqual(expectedStatus, result.Status);
        Assert.AreEqual(expectedWorkspace, result.Workspace?.Name);
        Assert.AreEqual(expectedRoot, result.MappedRoot);
        Assert.AreEqual(@"C:\cache\a.config", result.CacheFilePath);
    }

    [TestMethod]
    [DataRow("DEVBOX", true)]
    [DataRow("devbox", true)]
    [DataRow("OTHERBOX", false)]
    public void Classify_OnlyWorkspacesOfThisMachine(string computer, bool expectedMapped)
    {
        var cache = Cache(@"C:\cache\a.config", Older, Workspace("ws", computer, paths: @"C:\Src"));

        var result = TfvcPathClassifier.Classify(@"C:\Src\Site.cs", Machine, new[] { cache });

        Assert.AreEqual(expectedMapped, result.Status != TfvcPathStatus.NotMapped);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Classify_SameWorkspaceInSeveralCaches_NewestFileWins(bool newerFirst)
    {
        // One workspace, seen by two Visual Studio versions; it became local after the older cache was written.
        var older = Cache(@"C:\cache\old.config", Older, Workspace("ws", paths: @"C:\Src"));
        var newer = Cache(@"C:\cache\new.config", Newer, Workspace("ws", local: true, paths: @"C:\Src"));
        var files = newerFirst ? new[] { newer, older } : new[] { older, newer };

        var result = TfvcPathClassifier.Classify(@"C:\Src\Site.cs", Machine, files);

        Assert.AreEqual(TfvcPathStatus.LocalWorkspace, result.Status);
        Assert.AreEqual(@"C:\cache\new.config", result.CacheFilePath);
    }

    private static TfvcWorkspace Workspace(string name, string computer = Machine, bool local = false, params string[] paths) =>
        new("http://tfs.example.test/tfs/alpha", name, @"EXAMPLE\jdoe", computer, local, paths);

    private static TfvcCacheFile Cache(string path, DateTime lastWriteUtc, params TfvcWorkspace[] workspaces) =>
        new(path, lastWriteUtc, workspaces, error: null);
}
