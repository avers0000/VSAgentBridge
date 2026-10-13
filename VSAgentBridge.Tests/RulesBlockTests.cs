using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// RulesBlock: the bridge's rules block that init writes into ~\.claude\CLAUDE.md / ~\.config\opencode\AGENTS.md.
[TestClass]
internal sealed class RulesBlockTests
{
    private static readonly string[] TwoRoots = { @"D:\Projects", @"E:\Work" };

    private const string Sample =
        "intro\n<!-- vsab:begin -->\n# Rules\n  - <folder with VS Agent Bridge>\nmore\n<!-- vsab:end -->\ntail";

    private static string Render(string sample, params string[] roots)
    {
        Assert.IsTrue(RulesBlock.TryRender(sample, roots, out var block, out var error), error);
        Assert.IsNull(error);
        return block!;
    }

    private static string RenderError(string sample, params string[] roots)
    {
        Assert.IsFalse(RulesBlock.TryRender(sample, roots, out var block, out var error));
        Assert.IsNull(block);
        Assert.IsNotNull(error);
        return error!;
    }

    private static string SamplePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RulesBlock.SampleFileName);

    // ---- TryRender ----

    [TestMethod]
    public void Render_Synthetic_RootsFilledIn_TextOutsideMarkersIgnored()
    {
        Assert.AreEqual(
            "<!-- vsab:begin -->\n# Rules\n  - D:\\Projects\n  - E:\\Work\nmore\n<!-- vsab:end -->",
            Render(Sample, TwoRoots));
    }

    [TestMethod]
    public void Render_Crlf_BlockWithLf()
    {
        Assert.AreEqual(
            "<!-- vsab:begin -->\n# Rules\n  - D:\\Projects\nmore\n<!-- vsab:end -->",
            Render(Sample.Replace("\n", "\r\n"), @"D:\Projects"));
    }

    [TestMethod]
    public void Render_IndentOfPlaceholderKept()
    {
        var sample = "<!-- vsab:begin -->\n    - <folder with VS Agent Bridge>\n<!-- vsab:end -->";
        Assert.AreEqual("<!-- vsab:begin -->\n    - D:\\Projects\n    - E:\\Work\n<!-- vsab:end -->", Render(sample, TwoRoots));
    }

    [TestMethod]
    public void Render_BomBeforeBeginMarker_NotInBlock()
    {
        var block = Render("\uFEFF<!-- vsab:begin -->\r\n  - <folder with VS Agent Bridge>\r\n<!-- vsab:end -->", @"D:\Projects");
        Assert.AreEqual("<!-- vsab:begin -->\n  - D:\\Projects\n<!-- vsab:end -->", block);
    }

    [TestMethod]
    public void Render_RealSample()
    {
        var block = Render(File.ReadAllText(SamplePath), TwoRoots);
        StringAssert.StartsWith(block, "<!-- vsab:begin -->\n# Rules for folders with VS Agent Bridge only\n");
        StringAssert.EndsWith(block, "\n<!-- vsab:end -->");
        StringAssert.Contains(block, "\n  - D:\\Projects\n  - E:\\Work\n");
        Assert.IsFalse(block.Contains("<folder with VS Agent Bridge>"));
        Assert.IsFalse(block.Contains("\r"));
        Assert.IsFalse(block.Contains("\uFEFF"));
    }

    [TestMethod]
    public void Render_RealSample_ReadWithBom()
    {
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(SamplePath));
        Assert.AreEqual('\uFEFF', text[0], "the sample is expected to start with a BOM");
        StringAssert.StartsWith(Render(text, @"D:\Projects"), "<!-- vsab:begin -->\n");
    }

    [TestMethod]
    public void Render_NoRoots_Error()
    {
        StringAssert.Contains(RenderError(Sample), "no workspace roots");
    }

    [TestMethod]
    [DataRow("# Rules\n  - <folder with VS Agent Bridge>\n<!-- vsab:end -->")]
    [DataRow("<!-- vsab:begin -->\n  - <folder with VS Agent Bridge>\n")]
    [DataRow("<!-- vsab:begin -->\n<!-- vsab:begin -->\n  - <folder with VS Agent Bridge>\n<!-- vsab:end -->")]
    [DataRow("<!-- vsab:begin -->\n  - <folder with VS Agent Bridge>\n<!-- vsab:end -->\n<!-- vsab:end -->")]
    [DataRow("<!-- vsab:end -->\n  - <folder with VS Agent Bridge>\n<!-- vsab:begin -->")]
    public void Render_BrokenMarkers_Error(string sample)
    {
        StringAssert.Contains(RenderError(sample, @"D:\Projects"), "markers");
    }

    [TestMethod]
    [DataRow("<!-- vsab:begin -->\n# Rules\n<!-- vsab:end -->")]
    [DataRow("<!-- vsab:begin -->\n  - <folder with VS Agent Bridge>\n  - <folder with VS Agent Bridge>\n<!-- vsab:end -->")]
    [DataRow("  - <folder with VS Agent Bridge>\n<!-- vsab:begin -->\n# Rules\n<!-- vsab:end -->")]
    public void Render_BrokenPlaceholder_Error(string sample)
    {
        StringAssert.Contains(RenderError(sample, @"D:\Projects"), "must appear once between the markers");
    }

    // ---- Merge ----

    private const string Block = "<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->";

    private static string MergeOk(string? text, RulesBlockChange expected, string block = Block)
    {
        Assert.AreEqual(expected, RulesBlock.Merge(text, block, out var result));
        Assert.IsNotNull(result);
        return result!;
    }

    [TestMethod]
    public void Merge_NoFile_BlockWithCrlf()
    {
        Assert.AreEqual(
            "<!-- vsab:begin -->\r\nnew rules\r\n<!-- vsab:end -->\r\n",
            MergeOk(null, RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_EmptyFile_BlockWithCrlf()
    {
        Assert.AreEqual(
            "<!-- vsab:begin -->\r\nnew rules\r\n<!-- vsab:end -->\r\n",
            MergeOk("", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_WhitespaceOnlyLf_ReplacedByBlockWithLf()
    {
        Assert.AreEqual(
            "<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n",
            MergeOk("  \n\n", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_UserTextLf_AppendedAfterBlankLine()
    {
        Assert.AreEqual(
            "# Mine\nrule\n\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n",
            MergeOk("# Mine\nrule\n", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_UserTextCrlf_BlockConvertedToCrlf()
    {
        Assert.AreEqual(
            "# Mine\r\nrule\r\n\r\n<!-- vsab:begin -->\r\nnew rules\r\n<!-- vsab:end -->\r\n",
            MergeOk("# Mine\r\nrule\r\n", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_UserTextWithoutFinalNewline_LineBreakAdded()
    {
        Assert.AreEqual(
            "# Mine\nrule\n\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n",
            MergeOk("# Mine\nrule", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_OneLineUserText_Crlf()
    {
        Assert.AreEqual(
            "# Mine\r\n\r\n<!-- vsab:begin -->\r\nnew rules\r\n<!-- vsab:end -->\r\n",
            MergeOk("# Mine", RulesBlockChange.Appended));
    }

    [TestMethod]
    public void Merge_BlockInTheMiddle_ReplacedRestKept()
    {
        Assert.AreEqual(
            "a\n\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n\nb\n",
            MergeOk("a\n\n<!-- vsab:begin -->\nold\nrules\n<!-- vsab:end -->\n\nb\n", RulesBlockChange.Replaced));
    }

    [TestMethod]
    public void Merge_Crlf_ReplacedWithCrlf()
    {
        Assert.AreEqual(
            "a\r\n<!-- vsab:begin -->\r\nnew rules\r\n<!-- vsab:end -->\r\nb\r\n",
            MergeOk("a\r\n<!-- vsab:begin -->\r\nold\r\n<!-- vsab:end -->\r\nb\r\n", RulesBlockChange.Replaced));
    }

    [TestMethod]
    public void Merge_BlockAtEndWithoutFinalNewline_Kept()
    {
        Assert.AreEqual(
            "a\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->",
            MergeOk("a\n<!-- vsab:begin -->\nold\n<!-- vsab:end -->", RulesBlockChange.Replaced));
    }

    [TestMethod]
    public void Merge_MarkersWithSpaces_Recognized()
    {
        Assert.AreEqual(
            "a\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\nb\n",
            MergeOk("a\n  <!-- vsab:begin -->\t\nold\n <!-- vsab:end --> \nb\n", RulesBlockChange.Replaced));
    }

    [TestMethod]
    public void Merge_SameBlock_Unchanged()
    {
        var text = "# Mine\n\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n";
        Assert.AreEqual(text, MergeOk(text, RulesBlockChange.Unchanged));
    }

    [TestMethod]
    public void Merge_Twice_SecondUnchanged()
    {
        var once = MergeOk("# Mine\r\n", RulesBlockChange.Appended);
        Assert.AreEqual(once, MergeOk(once, RulesBlockChange.Unchanged));
    }

    [TestMethod]
    public void Merge_RealSample_OtherRoots_ReplacedUserTextKept()
    {
        var sample = File.ReadAllText(SamplePath);
        var user = "# My global rules\r\nBe brief.\r\n";
        var first = MergeOk(user, RulesBlockChange.Appended, Render(sample, TwoRoots));
        var second = MergeOk(first, RulesBlockChange.Replaced, Render(sample, @"F:\Other"));

        StringAssert.StartsWith(second, user + "\r\n<!-- vsab:begin -->\r\n");
        StringAssert.Contains(second, "\r\n  - F:\\Other\r\n");
        Assert.IsFalse(second.Contains(@"D:\Projects"));
        StringAssert.EndsWith(second, "<!-- vsab:end -->\r\n");
        Assert.AreEqual(1, second.Split(new[] { "<!-- vsab:begin -->" }, StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void Merge_MarkerInsideALine_NotAMarker()
    {
        Assert.AreEqual(
            "Write <!-- vsab:begin --> to mark it.\n\n<!-- vsab:begin -->\nnew rules\n<!-- vsab:end -->\n",
            MergeOk("Write <!-- vsab:begin --> to mark it.\n", RulesBlockChange.Appended));
    }

    [TestMethod]
    [DataRow("a\n<!-- vsab:begin -->\nold\n")]
    [DataRow("a\nold\n<!-- vsab:end -->\n")]
    [DataRow("<!-- vsab:begin -->\nx\n<!-- vsab:end -->\n<!-- vsab:begin -->\ny\n<!-- vsab:end -->\n")]
    [DataRow("<!-- vsab:end -->\nx\n<!-- vsab:begin -->\n")]
    [DataRow("<!-- vsab:begin -->\n<!-- vsab:begin -->\nx\n<!-- vsab:end -->\n")]
    public void Merge_Ambiguous_NoResult(string text)
    {
        Assert.AreEqual(RulesBlockChange.Ambiguous, RulesBlock.Merge(text, Block, out var result));
        Assert.IsNull(result);
    }
}
