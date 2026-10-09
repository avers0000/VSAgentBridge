using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileFormatTests
{
    [TestMethod]
    public void Constructor_OneSource_SetsSourceOnlyForGivenValues()
    {
        var format = new NewFileFormat(crlf: false, bom: null, finalNewline: null, NewFileFormat.SourceEditorConfig);

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        Assert.IsNull(format.Bom);
        Assert.IsNull(format.BomSource);
        Assert.IsNull(format.FinalNewlineSource);
        Assert.IsFalse(format.IsComplete);
    }

    [TestMethod]
    public void Or_TakesOwnValuesFirstAndLowerForTheRest()
    {
        var upper = new NewFileFormat(crlf: false, bom: null, finalNewline: null, NewFileFormat.SourceEditorConfig);
        var lower = new NewFileFormat(crlf: true, bom: false, finalNewline: null, NewFileFormat.SourceNeighbors, neighborNotUtf8: true);

        var format = upper.Or(lower).Or(NewFileFormat.VisualStudioDefault);

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.BomSource);
        Assert.AreEqual(true, format.FinalNewline);
        Assert.AreEqual(NewFileFormat.SourceDefault, format.FinalNewlineSource);
        Assert.IsTrue(format.NeighborNotUtf8);
        Assert.IsTrue(format.IsComplete);
    }

    [TestMethod]
    public void VisualStudioDefault_IsUtf8BomCrlfWithFinalNewline()
    {
        var format = NewFileFormat.VisualStudioDefault;

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
        Assert.AreEqual(true, format.FinalNewline);
        Assert.IsTrue(format.IsComplete);
    }

    [TestMethod]
    public void None_SetsNothing()
    {
        Assert.IsNull(NewFileFormat.None.Crlf);
        Assert.IsNull(NewFileFormat.None.Bom);
        Assert.IsNull(NewFileFormat.None.FinalNewline);
    }

    [TestMethod]
    public void WithBom_ReplacesOnlyTheBom()
    {
        var format = new NewFileFormat(true, false, true, NewFileFormat.SourceNeighbors, neighborNotUtf8: true)
            .WithBom(true, "rule");

        Assert.AreEqual(true, format.Bom);
        Assert.AreEqual("rule", format.BomSource);
        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(NewFileFormat.SourceNeighbors, format.CrlfSource);
        Assert.IsTrue(format.NeighborNotUtf8);
    }

    [TestMethod]
    public void ToString_NamesValuesAndSources()
    {
        var format = new NewFileFormat(crlf: true, bom: false, finalNewline: null, NewFileFormat.SourceNeighbors, neighborNotUtf8: true)
            .Or(NewFileFormat.VisualStudioDefault);

        Assert.AreEqual("crlf:neighbors nobom:neighbors final:default neighborNotUtf8", format.ToString());
    }

    [TestMethod]
    public void ToString_UnsetValues_ShowQuestionMarks()
    {
        Assert.AreEqual("?:- ?:- ?:-", NewFileFormat.None.ToString());
    }
}
