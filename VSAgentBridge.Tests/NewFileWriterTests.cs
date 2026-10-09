using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileWriterTests
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly DateTime Earlier = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private string _root = "";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, EditorConfigReader.FileName), new[] { "root = true" });
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    // ---- Apply / Encode ----

    [TestMethod]
    [DataRow("a\nb", true, true, "a\r\nb\r\n")]
    [DataRow("a\r\nb\rc\n", false, true, "a\nb\nc\n")]
    [DataRow("a\r\nb\n", true, true, "a\r\nb\r\n")]
    [DataRow("a\nb", true, false, "a\r\nb")]
    [DataRow("a\r\n", true, false, "a\r\n")]
    [DataRow("", true, true, "")]
    public void Apply_LineEndingsAndFinalNewline(string text, bool crlf, bool finalNewline, string expected)
    {
        var format = new NewFileFormat(crlf, bom: true, finalNewline, NewFileFormat.SourceDefault);

        Assert.AreEqual(expected, NewFileWriter.Apply(text, format));
    }

    [TestMethod]
    public void Encode_WithAndWithoutBom()
    {
        CollectionAssert.AreEqual(Bom.Concat(Encoding.UTF8.GetBytes("ж")).ToArray(), NewFileWriter.Encode("ж", bom: true));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("ж"), NewFileWriter.Encode("ж", bom: false));
    }

    // ---- Create ----

    [TestMethod]
    public void Create_NoNeighbors_VisualStudioDefaultAndNewFolders()
    {
        var path = Path.Combine(_root, "New", "Deeper", "X.cs");

        var (format, _, error) = NewFileWriter.Create(path, "class X\n{\n}", _root);

        Assert.IsNull(error, error);
        Assert.AreEqual(NewFileFormat.SourceDefault, format.BomSource);
        CollectionAssert.AreEqual(Bom.Concat(Encoding.UTF8.GetBytes("class X\r\n{\r\n}\r\n")).ToArray(), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Create_FollowsNeighbors()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dbo"));
        File.WriteAllBytes(Path.Combine(_root, "dbo", "Old.sql"), Encoding.ASCII.GetBytes("select 1\r\ngo\r\n"));
        var path = Path.Combine(_root, "dbo", "New.sql");

        var (format, _, error) = NewFileWriter.Create(path, "select 2\ngo\n", _root);

        Assert.IsNull(error, error);
        Assert.AreEqual(false, format.Bom);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("select 2\r\ngo\r\n"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Create_Existing_NeverOverwrites()
    {
        var path = Path.Combine(_root, "X.cs");
        File.WriteAllText(path, "original");

        var (_, _, error) = NewFileWriter.Create(path, "new", _root);

        Assert.IsNotNull(error);
        Assert.AreEqual("original", File.ReadAllText(path));
    }

    // ---- Normalize ----

    [TestMethod]
    public void Normalize_WrittenAsLf_GetsNeighborFormatAndKeepsWriteTime()
    {
        File.WriteAllBytes(Path.Combine(_root, "Old.cs"), Bom.Concat(Encoding.ASCII.GetBytes("a\r\n")).ToArray());
        var path = Path.Combine(_root, "New.cs");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("class X\n{\n}"));
        File.SetLastWriteTimeUtc(path, Earlier);

        var (format, _, changed, error) = NewFileWriter.Normalize(path, _root);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format!.BomSource);
        CollectionAssert.AreEqual(Bom.Concat(Encoding.UTF8.GetBytes("class X\r\n{\r\n}\r\n")).ToArray(), File.ReadAllBytes(path));
        Assert.AreEqual(Earlier, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void Normalize_AlreadyInFormat_NotRewritten()
    {
        var bytes = Bom.Concat(Encoding.UTF8.GetBytes("a\r\n")).ToArray();
        var path = Path.Combine(_root, "New.cs");
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, Earlier);

        var (_, _, changed, error) = NewFileWriter.Normalize(path, _root);

        Assert.IsNull(error, error);
        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(Earlier, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void Normalize_BomRemovedWhenNeighborsHaveNone()
    {
        File.WriteAllBytes(Path.Combine(_root, "Old.sql"), Encoding.ASCII.GetBytes("select 1\r\n"));
        var path = Path.Combine(_root, "New.sql");
        File.WriteAllBytes(path, Bom.Concat(Encoding.ASCII.GetBytes("select 2\r\n")).ToArray());

        var (_, _, changed, error) = NewFileWriter.Normalize(path, _root);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("select 2\r\n"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Normalize_NotUtf8_LeftAsWritten()
    {
        var bytes = new byte[] { 0x2D, 0x2D, 0x20, 0xCF, 0xF0, 0xE8, 0x0A };
        var path = Path.Combine(_root, "New.sql");
        File.WriteAllBytes(path, bytes);

        var (format, _, changed, error) = NewFileWriter.Normalize(path, _root);

        Assert.IsNotNull(error);
        Assert.IsNull(format);
        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Normalize_MissingFile_ReportsError()
    {
        var (_, _, changed, error) = NewFileWriter.Normalize(Path.Combine(_root, "Missing.cs"), _root);

        Assert.IsNotNull(error);
        Assert.IsFalse(changed);
    }
}
