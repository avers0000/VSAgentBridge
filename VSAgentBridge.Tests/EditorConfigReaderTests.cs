using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class EditorConfigReaderTests
{
    private string _root = "";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // Stops the lookup here: an .editorconfig above the temp folder must not change the results.
        Config("", "root = true");
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    // ---- Matches ----

    [TestMethod]
    [DataRow("*.cs", "a/b/X.cs", true)]
    [DataRow("*.cs", "X.csproj", false)]
    [DataRow("*.CS", "x.cs", true)]
    [DataRow("*.{cs,sql}", "dbo/X.sql", true)]
    [DataRow("*.{cs,sql}", "dbo/X.js", false)]
    [DataRow("{a,{b,c}}.txt", "c.txt", true)]
    [DataRow("src/*.cs", "src/X.cs", true)]
    [DataRow("src/*.cs", "other/src/X.cs", false)]
    [DataRow("src/*.cs", "src/deep/X.cs", false)]
    [DataRow("/src/*.cs", "src/X.cs", true)]
    [DataRow("**.sql", "a/b/c.sql", true)]
    [DataRow("a/**/b.cs", "a/b.cs", true)]
    [DataRow("a/**/b.cs", "a/x/y/b.cs", true)]
    [DataRow("*.[ch]", "x.c", true)]
    [DataRow("*.[!ch]", "x.c", false)]
    [DataRow("*", "anything/x.txt", true)]
    [DataRow("?.cs", "ab.cs", false)]
    [DataRow("?.cs", "a.cs", true)]
    [DataRow("file{1..3}.txt", "file2.txt", true)]
    [DataRow("{single}.txt", "{single}.txt", true)]
    [DataRow("a\\*b.txt", "a*b.txt", true)]
    [DataRow("a\\*b.txt", "axb.txt", false)]
    public void Matches_Globs(string glob, string relativePath, bool expected)
    {
        Assert.AreEqual(expected, EditorConfigReader.Matches(glob, relativePath), $"{glob} vs {relativePath}");
    }

    [TestMethod]
    public void Matches_UnclosedBracketOrBrace_IsLiteral()
    {
        Assert.IsTrue(EditorConfigReader.Matches("a[b.txt", "a[b.txt"));
        Assert.IsTrue(EditorConfigReader.Matches("a{b.txt", "a{b.txt"));
    }

    // ---- Read ----

    [TestMethod]
    public void Read_NoMatchingSection_NothingSet()
    {
        Config("", "root = true", "# comment", "; comment", "[*.js]", "end_of_line = lf");

        var format = EditorConfigReader.Read(File("X.cs"));

        Assert.IsNull(format.Crlf);
        Assert.IsNull(format.Bom);
        Assert.IsNull(format.FinalNewline);
    }

    [TestMethod]
    public void Read_MatchingSection_SetsTheThreeValues()
    {
        Config("", "root = true", "[*.sql]", "end_of_line = lf", "charset = utf-8", "insert_final_newline = false", "indent_style = tab");

        var format = EditorConfigReader.Read(File(@"dbo\Procs\X.sql"));

        Assert.AreEqual(false, format.Crlf);
        Assert.AreEqual(false, format.Bom);
        Assert.AreEqual(false, format.FinalNewline);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.CrlfSource);
        Assert.AreEqual(NewFileFormat.SourceEditorConfig, format.BomSource);
    }

    [TestMethod]
    public void Read_ValuesAreCaseInsensitive()
    {
        Config("", "ROOT = TRUE", "[*]", "End_Of_Line = CRLF", "Charset = UTF-8-BOM");

        var format = EditorConfigReader.Read(File("X.cs"));

        Assert.AreEqual(true, format.Crlf);
        Assert.AreEqual(true, format.Bom);
    }

    [TestMethod]
    public void Read_OtherCharset_LeavesBomUnset()
    {
        Config("", "root = true", "[*]", "charset = latin1");

        Assert.IsNull(EditorConfigReader.Read(File("X.cs")).Bom);
    }

    [TestMethod]
    public void Read_LaterSectionOverridesEarlier()
    {
        Config("", "root = true", "[*]", "end_of_line = lf", "[*.cs]", "end_of_line = crlf");

        Assert.AreEqual(true, EditorConfigReader.Read(File("X.cs")).Crlf);
        Assert.AreEqual(false, EditorConfigReader.Read(File("X.txt")).Crlf);
    }

    [TestMethod]
    public void Read_NearerFileOverridesFarther()
    {
        Config("", "root = true", "[*]", "end_of_line = lf", "charset = utf-8-bom");
        Config("Sub", "[*]", "end_of_line = crlf");

        var format = EditorConfigReader.Read(File(@"Sub\X.cs"));

        Assert.AreEqual(true, format.Crlf, "from Sub");
        Assert.AreEqual(true, format.Bom, "from the root file");
    }

    [TestMethod]
    public void Read_RootTrue_StopsTheLookup()
    {
        Config("Outer", "[*]", "charset = utf-8-bom");
        Config(@"Outer\Inner", "root = true", "[*]", "end_of_line = lf");

        var format = EditorConfigReader.Read(File(@"Outer\Inner\X.cs"));

        Assert.AreEqual(false, format.Crlf);
        Assert.IsNull(format.Bom, "the outer file is above root = true");
    }

    [TestMethod]
    public void Read_RootInsideASection_DoesNotStop()
    {
        Config("Outer", "[*]", "charset = utf-8-bom");
        Config(@"Outer\Inner", "[*]", "root = true");

        Assert.AreEqual(true, EditorConfigReader.Read(File(@"Outer\Inner\X.cs")).Bom);
    }

    [TestMethod]
    public void Read_Unset_RemovesAFartherValue()
    {
        Config("", "root = true", "[*]", "end_of_line = lf");
        Config("Sub", "[*]", "end_of_line = unset");

        Assert.IsNull(EditorConfigReader.Read(File(@"Sub\X.cs")).Crlf);
    }

    [TestMethod]
    public void Read_PathGlob_IsRelativeToItsFile()
    {
        Config("", "root = true", "[Sub/*.cs]", "end_of_line = lf");

        Assert.AreEqual(false, EditorConfigReader.Read(File(@"Sub\X.cs")).Crlf);
        Assert.IsNull(EditorConfigReader.Read(File(@"Other\Sub\X.cs")).Crlf);
    }

    [TestMethod]
    public void Read_FolderOfTheFileDoesNotExistYet_UsesParents()
    {
        Config("", "root = true", "[*.cs]", "end_of_line = lf");

        Assert.AreEqual(false, EditorConfigReader.Read(Path.Combine(_root, "New", "Deeper", "X.cs")).Crlf);
    }

    private void Config(string folder, params string[] lines)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(Path.Combine(dir, EditorConfigReader.FileName), lines);
    }

    // The path of a new file; its folder is created, the file itself is not.
    private string File(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
