using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class TfvcCacheReaderTests
{
    private const string ValidCache = """
        <VersionControlServer><Servers><ServerInfo uri="http://tfs.example.test/tfs/alpha">
          <WorkspaceInfo name="ws" computer="DEVBOX"><MappedPaths><MappedPath path="C:\Src" /></MappedPaths></WorkspaceInfo>
        </ServerInfo></Servers></VersionControlServer>
        """;

    // Plays the role of %LOCALAPPDATA%; one per test, so tests can run in parallel.
    private string _localAppData = "";

    [TestInitialize]
    public void CreateFolder()
    {
        _localAppData = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_localAppData);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_localAppData, recursive: true);

    [TestMethod]
    public void FindFiles_ReturnsKnownLayoutsOnly()
    {
        var expected = new[]
        {
            Put(@"Microsoft\Azure DevOps\12.0\Cache\Volatile\0a1b_http\VersionControl.config"),
            Put(@"Microsoft\Azure DevOps\8.0\Cache\Volatile\2c3d_http\VersionControl.config"),
            Put(@"Microsoft\Azure DevOps\8.0\Cache\Volatile\4e5f_http\VersionControl.config"),
            Put(@"Microsoft\Team Foundation\8.0\Cache\VersionControl.config"),
        };

        Put(@"Microsoft\Azure DevOps\8.0\Cache\Volatile\2c3d_http\Nested\VersionControl.config"); // too deep
        Put(@"Microsoft\Azure DevOps\8.0\VersionControl.config");                                 // wrong level
        Put(@"Microsoft\Azure DevOps\8.0\Cache\Volatile\6a7b_http\VersionControl.config.bak");    // wrong name
        Put(@"Microsoft\Other Product\8.0\Cache\VersionControl.config");                          // unknown product
        Put(@"Azure DevOps\8.0\Cache\VersionControl.config");                                     // outside Microsoft

        CollectionAssert.AreEquivalent(expected, TfvcCacheReader.FindFiles(_localAppData).ToArray());
    }

    [TestMethod]
    public void FindFiles_NoMicrosoftFolder_ReturnsEmpty() =>
        Assert.IsEmpty(TfvcCacheReader.FindFiles(_localAppData));

    [TestMethod]
    public void Read_FileOpenForWritingElsewhere_ParsesIt()
    {
        var path = Put(@"VersionControl.config", ValidCache);
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        // Visual Studio keeps the file open for writing while it works.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            var file = TfvcCacheReader.Read(path);

            Assert.IsNull(file.Error);
            Assert.AreEqual(path, file.FilePath);
            Assert.AreEqual(written, file.LastWriteTimeUtc);
            Assert.AreEqual("ws", file.Workspaces.Single().Name);
        }
    }

    [TestMethod]
    [DataRow("<VersionControlServer><Servers>", "XmlException")]
    [DataRow(null, "FileNotFoundException")]
    public void Read_UnreadableFile_ReportsErrorInsteadOfThrowing(string? content, string errorType)
    {
        var path = content == null
            ? Path.Combine(_localAppData, "missing.config")
            : Put("VersionControl.config", content);

        var file = TfvcCacheReader.Read(path);

        Assert.IsEmpty(file.Workspaces);
        Assert.IsNotNull(file.Error);
        Assert.IsTrue(file.Error.StartsWith(errorType + ":", StringComparison.Ordinal), file.Error);
    }

    private string Put(string relativePath, string content = ValidCache)
    {
        var path = Path.Combine(_localAppData, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
