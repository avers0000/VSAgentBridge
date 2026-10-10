using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

/// <summary>NewFileWriter for an existing file: Capture in "before", Restore in "after" (slice 6 results, §1.13).</summary>
[TestClass]
internal sealed class NewFileWriterExistingTests
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly DateTime Earlier = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    // "Привет" in windows-1251: not valid UTF-8.
    private static readonly byte[] Cp1251 = { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 };

    private string _root = "";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? Bom.Concat(Encoding.UTF8.GetBytes(text)).ToArray() : Encoding.UTF8.GetBytes(text);

    private string WriteFile(byte[] bytes)
    {
        var path = Path.Combine(_root, "X.cs");
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, Earlier);
        return path;
    }

    // ---- Capture ----

    [TestMethod]
    [DataRow("a\r\nb\r\n", true, "existing bom crlf final")]
    [DataRow("a\nb", false, "existing nobom lf nofinal")]
    [DataRow("a\nb\n", true, "existing bom lf final")]
    [DataRow("a\r\nb\n", false, "existing nobom mixed final")]
    [DataRow("a\rb\r\n", false, "existing nobom mixed final")]
    [DataRow("a\r", false, "existing nobom mixed final")]
    [DataRow("abc", false, "existing nobom mixed nofinal")]
    [DataRow("", true, "existing bom mixed nofinal")]
    [DataRow("", false, "existing nobom mixed nofinal")]
    [DataRow("\r\n", false, "existing nobom crlf final")]
    [DataRow("ж\r\nё", true, "existing bom crlf nofinal")]
    public void Capture_Format(string text, bool bom, string expected) =>
        Assert.AreEqual(expected, NewFileWriter.Capture(Utf8(text, bom)).ToString());

    [TestMethod]
    public void Capture_LegacyCodePage_NotUtf8()
    {
        Assert.AreSame(WriteState.ExistingNotUtf8, NewFileWriter.Capture(Cp1251));
        Assert.AreSame(WriteState.ExistingNotUtf8, NewFileWriter.Capture(Bom.Concat(Cp1251).ToArray()));
    }

    [TestMethod]
    public void Capture_Path_ReadsTheFile()
    {
        var path = WriteFile(Utf8("a\r\nb\r\n", bom: true));

        var (state, error) = NewFileWriter.Capture(path);

        Assert.IsNull(error, error);
        Assert.AreEqual("existing bom crlf final", state!.ToString());
    }

    [TestMethod]
    public void Capture_MissingFile_Error()
    {
        var (state, error) = NewFileWriter.Capture(Path.Combine(_root, "Missing.cs"));

        Assert.IsNull(state);
        Assert.IsNotNull(error);
    }

    // ---- ApplyExisting ----

    [TestMethod]
    [DataRow("a\nb\n", "existing bom crlf final", "a\r\nb\r\n")]
    [DataRow("a\nb", "existing bom crlf final", "a\r\nb\r\n")]
    [DataRow("a\r\nb\r\n", "existing nobom lf nofinal", "a\nb")]
    [DataRow("a\nb\n\n", "existing nobom lf nofinal", "a\nb\n")]
    [DataRow("a\r\nb\r\n", "existing nobom crlf nofinal", "a\r\nb")]
    [DataRow("a\r\nb\nc", "existing nobom mixed final", "a\r\nb\nc\n")]
    [DataRow("a\nb\r\nc", "existing nobom mixed final", "a\nb\r\nc\r\n")]
    [DataRow("abc", "existing nobom mixed final", "abc\r\n")]
    [DataRow("a\nb\n", "existing nobom mixed nofinal", "a\nb")]
    [DataRow("a\r", "existing nobom mixed nofinal", "a")]
    [DataRow("a\r\nb\n", "existing nobom mixed final", "a\r\nb\n")]
    [DataRow("", "existing bom crlf final", "")]
    public void ApplyExisting_LineEndingsAndFinalNewline(string text, string state, string expected) =>
        Assert.AreEqual(expected, NewFileWriter.ApplyExisting(text, WriteState.Parse(state)!));

    // ---- Restore ----

    [TestMethod]
    public void Restore_WrittenAsLf_GetsFormatBackAndKeepsWriteTime()
    {
        var path = WriteFile(Utf8("line A changed\nline B"));

        var (changed, error) = NewFileWriter.Restore(path, WriteState.Parse("existing bom crlf final")!);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(Utf8("line A changed\r\nline B\r\n", bom: true), File.ReadAllBytes(path));
        Assert.AreEqual(Earlier, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void Restore_AlreadyInFormat_NotRewritten()
    {
        var bytes = Utf8("a\r\nb\r\n", bom: true);
        var path = WriteFile(bytes);

        var (changed, error) = NewFileWriter.Restore(path, WriteState.Parse("existing bom crlf final")!);

        Assert.IsNull(error, error);
        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(Earlier, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void Restore_BomRemovedWhenOriginalHadNone()
    {
        var path = WriteFile(Utf8("a\n", bom: true));

        var (changed, error) = NewFileWriter.Restore(path, WriteState.Parse("existing nobom lf final")!);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(Utf8("a\n"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Restore_MixedOriginal_LineEndingsKept()
    {
        var path = WriteFile(Utf8("a\r\nb\nc\n"));

        var (changed, error) = NewFileWriter.Restore(path, WriteState.Parse("existing bom mixed nofinal")!);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(Utf8("a\r\nb\nc", bom: true), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Restore_OriginalNotUtf8_LeftAsWritten()
    {
        var bytes = Utf8("a\nb\n");
        var path = WriteFile(bytes);

        var (changed, error) = NewFileWriter.Restore(path, WriteState.ExistingNotUtf8);

        Assert.IsFalse(changed);
        Assert.IsNotNull(error);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Restore_WrittenNotUtf8_LeftAsWritten()
    {
        var bytes = Cp1251.Concat(new byte[] { 0x0A }).ToArray();
        var path = WriteFile(bytes);

        var (changed, error) = NewFileWriter.Restore(path, WriteState.Parse("existing bom crlf final")!);

        Assert.IsFalse(changed);
        Assert.IsNotNull(error);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Restore_MissingFile_Error()
    {
        var (changed, error) = NewFileWriter.Restore(Path.Combine(_root, "Missing.cs"), WriteState.Parse("existing bom crlf final")!);

        Assert.IsFalse(changed);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Restore_NewState_Throws() =>
        Assert.ThrowsExactly<ArgumentException>(() => NewFileWriter.Restore(Path.Combine(_root, "X.cs"), WriteState.New));

    [TestMethod]
    public void CaptureThenRestore_OnlyTheChangedLineDiffers()
    {
        // As in §3a W1: Write keeps the text but drops the BOM and turns CRLF into LF.
        var path = WriteFile(Utf8("namespace N\r\n{\r\n\tline A\r\n}\r\n", bom: true));
        var (state, _) = NewFileWriter.Capture(path);
        File.WriteAllBytes(path, Utf8("namespace N\n{\n\tline A changed\n}\n"));

        var (changed, error) = NewFileWriter.Restore(path, state!);

        Assert.IsNull(error, error);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(Utf8("namespace N\r\n{\r\n\tline A changed\r\n}\r\n", bom: true), File.ReadAllBytes(path));
    }
}
