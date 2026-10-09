using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileFormatResolverTests
{
    private static NewFileFormat EditorConfig(bool? crlf, bool? bom, bool? finalNewline = null) =>
        new(crlf, bom, finalNewline, NewFileFormat.SourceEditorConfig);

    private static NewFileFormat Neighbors(bool? crlf, bool? bom, bool notUtf8 = false) =>
        new(crlf, bom, null, NewFileFormat.SourceNeighbors, notUtf8);

    [TestMethod]
    public void Resolve_NothingKnown_GivesVisualStudioDefault()
    {
        var format = NewFileFormatResolver.Resolve(NewFileFormat.None, NewFileFormat.None, "text");

        Assert.IsTrue(format.IsComplete);
        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
        Assert.AreEqual(true, format.FinalNewline);
        Assert.AreEqual(NewFileFormat.SourceDefault, format.CrlfSource);
        Assert.AreEqual(NewFileFormat.SourceDefault, format.BomSource);
    }

    [TestMethod]
    public void Resolve_EditorConfigWinsOverNeighbors()
    {
        var format = NewFileFormatResolver.Resolve(
            EditorConfig(crlf: false, bom: true, finalNewline: false), Neighbors(crlf: true, bom: false), "text");

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(true, format.Bom);
        Assert.AreEqual(false, format.FinalNewline);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.FinalNewlineSource);
    }

    [TestMethod]
    public void Resolve_ValueByValue()
    {
        var format = NewFileFormatResolver.Resolve(EditorConfig(crlf: false, bom: null), Neighbors(crlf: true, bom: false), "text");

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.BomSource);
        Assert.AreEqual(true, format.FinalNewline);
        Assert.AreEqual(NewFileFormat.SourceDefault, format.FinalNewlineSource);
    }

    [TestMethod]
    public void Resolve_NonAsciiNextToLegacyFiles_GetsABom()
    {
        var format = NewFileFormatResolver.Resolve(NewFileFormat.None, Neighbors(crlf: true, bom: false, notUtf8: true), "-- привет");

        Assert.AreEqual(true, format.Bom);
        Assert.AreEqual(NewFileFormatResolver.SourceNonAsciiNextToLegacy, format.BomSource);
    }

    [TestMethod]
    public void Resolve_AsciiNextToLegacyFiles_KeepsNoBom()
    {
        var format = NewFileFormatResolver.Resolve(NewFileFormat.None, Neighbors(crlf: true, bom: false, notUtf8: true), "select 1");

        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.BomSource);
    }

    [TestMethod]
    public void Resolve_NonAsciiNextToUtf8FilesWithoutBom_KeepsNoBom()
    {
        var format = NewFileFormatResolver.Resolve(NewFileFormat.None, Neighbors(crlf: true, bom: false), "// привет");

        Assert.AreEqual(false, format.Bom);
    }

    [TestMethod]
    public void Resolve_ExplicitEditorConfigCharset_IsObeyed()
    {
        var format = NewFileFormatResolver.Resolve(
            EditorConfig(crlf: null, bom: false), Neighbors(crlf: true, bom: false, notUtf8: true), "-- привет");

        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.BomSource);
    }

    // ---- With the disk ----

    [TestMethod]
    public void Resolve_FromDisk_UsesNeighborsAndMeasuresTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "dbo"));
            File.WriteAllLines(Path.Combine(root, EditorConfigReader.FileName), new[] { "root = true" });
            File.WriteAllBytes(Path.Combine(root, "dbo", "Old.sql"), Encoding.ASCII.GetBytes("select 1\r\n"));

            var format = NewFileFormatResolver.Resolve(Path.Combine(root, "dbo", "New.sql"), root, "select 2", out var ms);

            Assert.IsTrue(format.IsComplete);
            Assert.AreEqual(false, format.Bom);
            Assert.AreEqual(true, format.Crlf);
            Assert.AreEqual(NewFileFormat.SourceNeighbors, format.BomSource);
            Assert.IsTrue(ms >= 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_FromDisk_CompleteEditorConfig_IgnoresNeighbors()
    {
        var root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllLines(Path.Combine(root, EditorConfigReader.FileName),
                new[] { "root = true", "[*]", "end_of_line = lf", "charset = utf-8-bom" });
            File.WriteAllBytes(Path.Combine(root, "Old.cs"), Encoding.ASCII.GetBytes("a\r\n"));

            var format = NewFileFormatResolver.Resolve(Path.Combine(root, "New.cs"), root, "x", out _);

            Assert.AreEqual(false, format.Crlf);
            Assert.AreEqual(true, format.Bom);
            Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
