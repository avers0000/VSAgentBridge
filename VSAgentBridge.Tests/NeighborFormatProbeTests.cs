using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NeighborFormatProbeTests
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    private string _scope = "";

    [TestInitialize]
    public void CreateScope()
    {
        _scope = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scope);
    }

    [TestCleanup]
    public void DeleteScope() => Directory.Delete(_scope, recursive: true);

    [TestMethod]
    public void Probe_BomCrlfNeighbors()
    {
        Write(@"A\One.cs", bom: true, "a\r\nb\r\n");
        Write(@"A\Two.cs", bom: true, "a\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
        Assert.IsNull(format.FinalNewline, "neighbors do not give the final newline");
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.CrlfSource);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.BomSource);
        Assert.IsFalse(format.NeighborNotUtf8);
    }

    [TestMethod]
    public void Probe_SqlWithoutBom()
    {
        Write(@"dbo\Procs\P1.sql", bom: false, "select 1\r\ngo\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"dbo\Procs\P2.sql"), _scope);

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(false, format.Bom);
        Assert.IsFalse(format.NeighborNotUtf8, "ASCII is valid UTF-8");
    }

    [TestMethod]
    public void Probe_MajorityDecides()
    {
        Write(@"A\1.js", bom: false, "a\nb\n");
        Write(@"A\2.js", bom: false, "a\n");
        Write(@"A\3.js", bom: true, "a\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.js"), _scope);

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(false, format.Bom);
    }

    [TestMethod]
    public void Probe_Tie_GivesVisualStudioDefault()
    {
        Write(@"A\1.cs", bom: false, "a\n");
        Write(@"A\2.cs", bom: true, "a\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
    }

    [TestMethod]
    public void Probe_TheNewFileItself_IsNotCounted()
    {
        // The hook's "after": the write tool already wrote the new file (LF, no BOM).
        Write(@"A\Old.cs", bom: true, "a\r\n");
        var newFile = Write(@"A\New.cs", bom: false, "a\nb\nc\n");

        var format = NeighborFormatProbe.Probe(newFile, _scope);

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
    }

    [TestMethod]
    public void Probe_OtherExtensions_AreIgnored()
    {
        Write(@"A\App.csproj", bom: false, "<Project>\n</Project>\n");
        Write(@"A\Notes.txt", bom: false, "a\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.IsNull(format.Crlf);
        Assert.IsNull(format.Bom);
    }

    [TestMethod]
    public void Probe_NearestFolderFirst()
    {
        for (var i = 0; i < NeighborFormatProbe.MaxFiles; i++)
        {
            Write($@"A\B\Near{i}.cs", bom: false, "a\n");
            Write($@"A\Far{i}.cs", bom: true, "a\r\n");
        }

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\B\New.cs"), _scope);

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(false, format.Bom);
    }

    [TestMethod]
    public void Probe_GoesUpTheFolders()
    {
        Write(@"A\Old.cs", bom: true, "a\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\B\C\New.cs"), _scope);

        Assert.AreEqual(true, format.Bom);
    }

    [TestMethod]
    public void Probe_NewFolders_StartAtTheNearestExistingOne()
    {
        Write(@"A\Old.sql", bom: false, "a\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New\Deeper\X.sql"), _scope);

        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(true, format.Crlf);
    }

    [TestMethod]
    public void Probe_NotAboveTheScope()
    {
        var project = Path.Combine(_scope, "Project");
        Write(@"Above.cs", bom: false, "a\n");
        Directory.CreateDirectory(project);

        var format = NeighborFormatProbe.Probe(Path.Combine(project, "New.cs"), project);

        Assert.IsNull(format.Crlf);
        Assert.IsNull(format.Bom);
    }

    [TestMethod]
    public void Probe_NoneOnThePath_SearchesTheWholeScope()
    {
        Write(@"Other\Deep\Old.cs", bom: false, "a\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(false, format.Bom);
    }

    [TestMethod]
    [DataRow("bin")]
    [DataRow("obj")]
    [DataRow(".vs")]
    [DataRow("node_modules")]
    public void Probe_WholeScopeSearch_SkipsBuildFolders(string folder)
    {
        Write($@"{folder}\Generated.cs", bom: false, "a\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.IsNull(format.Crlf);
    }

    [TestMethod]
    public void Probe_LegacyCodePageNeighbor_IsFlagged()
    {
        // "-- " + Cyrillic in windows-1251 (0xCF 0xF0 0xE8): not valid UTF-8.
        WriteBytes(@"dbo\Old.sql", new byte[] { 0x2D, 0x2D, 0x20, 0xCF, 0xF0, 0xE8, 0x0D, 0x0A });

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"dbo\New.sql"), _scope);

        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(true, format.Crlf);
        Assert.IsTrue(format.NeighborNotUtf8);
    }

    [TestMethod]
    public void Probe_Utf8WithoutBomCyrillic_IsNotFlagged()
    {
        Write(@"A\Old.cs", bom: false, "// привет\r\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.AreEqual(false, format.Bom);
        Assert.IsFalse(format.NeighborNotUtf8);
    }

    [TestMethod]
    public void Probe_LargeFile_CutInsideACharacter_IsStillUtf8()
    {
        // Two-byte characters across the 64 KB sample boundary.
        var text = new StringBuilder();
        while (Encoding.UTF8.GetByteCount(text.ToString()) < 70 * 1024)
            text.Append("ж\r\n");
        Write(@"A\Big.cs", bom: false, "xyz" + text); // 3 + 4n: byte 65535 is the first byte of a "ж"

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.IsFalse(format.NeighborNotUtf8);
        Assert.AreEqual(true, format.Crlf);
    }

    [TestMethod]
    public void Probe_FilesWithoutLineBreaks_GiveNoLineEnding()
    {
        Write(@"A\One.cs", bom: true, "single line");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\New.cs"), _scope);

        Assert.IsNull(format.Crlf);
        Assert.AreEqual(true, format.Bom);
    }

    [TestMethod]
    public void Probe_NoExtension_NothingSet()
    {
        Write(@"A\Makefile", bom: false, "a\n");

        var format = NeighborFormatProbe.Probe(Path.Combine(_scope, @"A\Makefile2"), _scope);

        Assert.IsNull(format.Crlf);
        Assert.IsNull(format.Bom);
    }

    private string Write(string relativePath, bool bom, string text)
    {
        var body = new UTF8Encoding(false).GetBytes(text);
        return WriteBytes(relativePath, bom ? Bom.Concat(body).ToArray() : body);
    }

    private string WriteBytes(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(_scope, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
