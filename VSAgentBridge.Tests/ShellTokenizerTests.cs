using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class ShellTokenizerTests
{
    [TestMethod]
    public void Words_SplitOnWhitespace()
    {
        AssertSegments("rm  -rf\tbin obj", "rm|-rf|bin|obj");
    }

    [TestMethod]
    public void Separators_EndSegments()
    {
        AssertSegments("a 1; b && c || d | e & f\ng", "a|1", "b", "c", "d", "e", "f", "g");
    }

    [TestMethod]
    public void Empty_NoSegments()
    {
        Assert.AreEqual(0, ShellTokenizer.Split("").Count);
        Assert.AreEqual(0, ShellTokenizer.Split("   \r\n ; ;").Count);
        Assert.AreEqual(0, ShellTokenizer.Split(null!).Count);
    }

    [TestMethod]
    public void DoubleQuotes_KeepSpacesAndBackslashes()
    {
        var t = Single("\"D:\\a b\\x.cs\"");
        Assert.AreEqual(@"D:\a b\x.cs", t.Text);
        Assert.IsTrue(t.HasQuotes);
        Assert.IsFalse(t.HasExpansion);
    }

    [TestMethod]
    public void DoubleQuotes_TrailingBackslashBeforeSpace_IsAPath()
    {
        AssertSegments("rd \"D:\\dir\\\" /s", @"rd|D:\dir\|/s");
    }

    [TestMethod]
    public void DoubleQuotes_Escapes()
    {
        Assert.AreEqual("say \"hi\"", Single("\"say \\\"hi\\\"\"").Text);  // bash \"
        Assert.AreEqual("say \"hi\"", Single("\"say `\"hi`\"\"").Text);    // PowerShell `"
        Assert.AreEqual("a\"b", Single("\"a\"\"b\"").Text);                // PowerShell/cmd ""
    }

    [TestMethod]
    public void SingleQuotes_AreLiteral_DoubledQuoteIsAQuote()
    {
        var t = Single("'it''s $x'");
        Assert.AreEqual("it's $x", t.Text);
        Assert.IsFalse(t.HasExpansion);
    }

    [TestMethod]
    public void QuoteInsideWord_JoinsTheWord()
    {
        Assert.AreEqual("--message=hello world", Single("--message=\"hello world\"").Text);
    }

    [TestMethod]
    public void UnclosedQuote_RunsToTheEnd()
    {
        AssertSegments("rm \"abc def", "rm|abc def");
        AssertSegments("rm 'abc", "rm|abc");
    }

    [TestMethod]
    public void Variables_MarkExpansion()
    {
        Assert.IsTrue(Single("$x").HasExpansion);
        Assert.IsTrue(Single("$env:TEMP\\a.txt").HasExpansion);
        Assert.IsTrue(Single("${x}y").HasExpansion);
        Assert.IsTrue(Single("\"$x\\a\"").HasExpansion);
        Assert.IsTrue(Single("%TEMP%\\a.txt").HasExpansion);
        Assert.IsFalse(Single("$/Project/x.cs").HasExpansion); // TFVC server path
        Assert.IsFalse(Single("a%b").HasExpansion);
    }

    [TestMethod]
    public void CommandSubstitution_IsItsOwnSegment()
    {
        AssertSegments("echo $(rm x)", "echo|$", "rm|x");
    }

    [TestMethod]
    public void ScriptBlocks_AreSegments()
    {
        AssertSegments("Get-ChildItem | % { Remove-Item $_ }", "Get-ChildItem", "%", "Remove-Item|$_");
        AssertSegments("ls | %{rm $_}", "ls", "%", "rm|$_");
        AssertSegments("ForEach-Object -Process { rm $_ }", "ForEach-Object|-Process", "rm|$_");
    }

    [TestMethod]
    public void BracesInsideAWord_StayText()
    {
        AssertSegments("echo file.{cs,bak}", "echo|file.{cs,bak}");
        AssertSegments("find . -exec rm {} \\;", "find|.|-exec|rm|{}|;");
    }

    [TestMethod]
    public void Comment_IsSkipped()
    {
        AssertSegments("echo a # rm x\nls", "echo|a", "ls");
        AssertSegments("echo a#b", "echo|a#b");
    }

    [TestMethod]
    public void LineContinuation_JoinsLines()
    {
        AssertSegments("Remove-Item `\r\n  x", "Remove-Item|x");
        AssertSegments("rm \\\n x", "rm|x");
        AssertSegments("del ^\r\n x", "del|x");
    }

    [TestMethod]
    public void Redirects_AreTokens()
    {
        AssertSegments("echo hi > x.cs", "echo|hi|[>]|x.cs");
        AssertSegments("echo hi>>x.cs", "echo|hi|[>>]|x.cs");
        AssertSegments("build 2>&1", "build|[2>&1]");
        AssertSegments("git status 2>$null", "git|status|[2>]|$null");
        AssertSegments("x *> log", "x|[*>]|log");
        AssertSegments("x &> log", "x|[&>]|log");
        AssertSegments("x >&2", "x|[>&2]");
        AssertSegments("sort < in.txt", "sort|[<]|in.txt");
        AssertSegments("echo \"a > b\"", "echo|a > b");
    }

    [TestMethod]
    public void HereString_IsOneQuotedWord()
    {
        var segments = ShellTokenizer.Split("Set-Content x.cs @\"\r\nline 1\r\nRemove-Item y\r\n\"@\r\nls");

        Assert.AreEqual(2, segments.Count);
        var body = segments[0][2];
        Assert.AreEqual("line 1\r\nRemove-Item y", body.Text);
        Assert.IsTrue(body.HasQuotes);
        Assert.AreEqual("ls", segments[1][0].Text);
    }

    [TestMethod]
    public void HereString_Empty_AndUnclosed()
    {
        Assert.AreEqual("", ShellTokenizer.Split("@'\n'@")[0][0].Text);
        Assert.AreEqual("abc\nrm x", ShellTokenizer.Split("@'\nabc\nrm x")[0][0].Text);
    }

    [TestMethod]
    public void Heredoc_BodyIsSkipped()
    {
        AssertSegments("cat > x.cs <<'EOF'\nrm y\n  EOF\nEOF\necho done", "cat|[>]|x.cs", "echo|done");
        AssertSegments("cat <<-END > x\n\trm y\n\tEND\nls", "cat|[>]|x", "ls");
    }

    [TestMethod]
    public void Positions_PointIntoTheText()
    {
        const string text = "pwsh -c Remove-Item \"a b\"";
        var tokens = ShellTokenizer.Split(text)[0];

        Assert.AreEqual("Remove-Item", text.Substring(tokens[2].Start, tokens[2].End - tokens[2].Start));
        Assert.AreEqual("\"a b\"", text.Substring(tokens[3].Start, tokens[3].End - tokens[3].Start));
    }

    [TestMethod]
    [DataRow("\"'\"'\"")]
    [DataRow("$((")]
    [DataRow("@\"")]
    [DataRow("<<")]
    [DataRow("<< \n")]
    [DataRow(">")]
    [DataRow("2>&")]
    [DataRow("}}}{{{")]
    [DataRow("${")]
    [DataRow("`")]
    [DataRow("\\")]
    [DataRow("@'\r")]
    [DataRow("кириллица > файл.txt")]
    public void AnyText_DoesNotThrow(string text)
    {
        ShellTokenizer.Split(text);
    }

    private static ShellToken Single(string text)
    {
        var segments = ShellTokenizer.Split(text);
        Assert.AreEqual(1, segments.Count, text);
        Assert.AreEqual(1, segments[0].Count, text);
        return segments[0][0];
    }

    // Each expected segment: tokens joined by '|', a redirection written as [op].
    private static void AssertSegments(string text, params string[] expected)
    {
        var actual = ShellTokenizer.Split(text)
            .Select(s => string.Join("|", s.Select(t => t.IsRedirect ? $"[{t.Text}]" : t.Text)))
            .ToArray();
        CollectionAssert.AreEqual(expected, actual, text);
    }
}
