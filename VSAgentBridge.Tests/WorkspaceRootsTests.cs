using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// WorkspaceRoots: the folder list init gets from --roots or the console ("D:\A;D:\B").
[TestClass]
internal sealed class WorkspaceRootsTests
{
    private static IReadOnlyList<string> ParseOk(string input)
    {
        Assert.IsTrue(WorkspaceRoots.TryParse(input, out var roots, out var error), error);
        Assert.IsNull(error);
        return roots;
    }

    private static string ParseError(string? input)
    {
        Assert.IsFalse(WorkspaceRoots.TryParse(input, out var roots, out var error));
        Assert.AreEqual(0, roots.Count);
        Assert.IsNotNull(error);
        return error!;
    }

    [TestMethod]
    public void OneFolder()
    {
        CollectionAssert.AreEqual(new[] { @"D:\Projects" }, ParseOk(@"D:\Projects").ToArray());
    }

    [TestMethod]
    public void SeveralFolders_OrderKept()
    {
        CollectionAssert.AreEqual(
            new[] { @"E:\Work", @"D:\Projects" },
            ParseOk(@"E:\Work;D:\Projects").ToArray());
    }

    [TestMethod]
    public void SpacesAndEmptyEntries_Ignored()
    {
        CollectionAssert.AreEqual(
            new[] { @"D:\Projects", @"E:\Work" },
            ParseOk(@" ;; D:\Projects ; ;E:\Work ;").ToArray());
    }

    [TestMethod]
    public void SpaceInsideAPath_Kept()
    {
        CollectionAssert.AreEqual(new[] { @"D:\My Projects" }, ParseOk(@"D:\My Projects").ToArray());
    }

    [TestMethod]
    [DataRow(@"D:\Projects\", @"D:\Projects")]
    [DataRow(@"D:/Projects", @"D:\Projects")]
    [DataRow(@"D:/Projects/", @"D:\Projects")]
    [DataRow(@"D:\Projects\.\App\..", @"D:\Projects")]
    [DataRow(@"D:\", @"D:\")]
    [DataRow(@"D:/", @"D:\")]
    [DataRow(@"\\server\share\", @"\\server\share")]
    [DataRow(@"//server/share/folder", @"\\server\share\folder")]
    public void Normalized(string input, string expected)
    {
        CollectionAssert.AreEqual(new[] { expected }, ParseOk(input).ToArray());
    }

    [TestMethod]
    public void Duplicates_CaseInsensitive_FirstKept()
    {
        CollectionAssert.AreEqual(
            new[] { @"D:\Projects", @"E:\Work" },
            ParseOk(@"D:\Projects;E:\Work;d:\projects\;D:/PROJECTS").ToArray());
    }

    [TestMethod]
    public void NestedFolders_BothKept()
    {
        CollectionAssert.AreEqual(
            new[] { @"D:\Projects", @"D:\Projects\App" },
            ParseOk(@"D:\Projects;D:\Projects\App").ToArray());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(";")]
    [DataRow(" ; ; ")]
    public void Nothing_Error(string? input)
    {
        StringAssert.Contains(ParseError(input), "at least one folder");
    }

    [TestMethod]
    [DataRow("Projects")]
    [DataRow(@"..\Projects")]
    [DataRow("D:Projects")]
    [DataRow(@"\Projects")]
    [DataRow("D:")]
    public void NotAbsolute_Error(string input)
    {
        var error = ParseError(input);
        StringAssert.Contains(error, "Not an absolute folder path");
        StringAssert.Contains(error, $"\"{input}\"");
    }

    [TestMethod]
    public void InvalidCharacters_Error()
    {
        StringAssert.Contains(ParseError("D:\\Pro|jects"), "\"D:\\Pro|jects\"");
    }

    [TestMethod]
    public void OneBadEntry_WholeInputRejected_AllBadListed()
    {
        var error = ParseError(@"D:\Projects;Work;E:\Ok;D:Temp");
        StringAssert.Contains(error, "\"Work\"");
        StringAssert.Contains(error, "\"D:Temp\"");
        Assert.IsFalse(error.Contains("\"D:\\Projects\""));
        Assert.IsFalse(error.Contains("\"E:\\Ok\""));
    }

    [TestMethod]
    public void Format_JoinsWithSemicolon()
    {
        Assert.AreEqual(@"D:\Projects;E:\Work", WorkspaceRoots.Format(new[] { @"D:\Projects", @"E:\Work" }));
    }

    [TestMethod]
    public void Format_ThenParse_SameList()
    {
        var roots = new[] { @"D:\My Projects", @"\\server\share", @"E:\" };
        CollectionAssert.AreEqual(roots, ParseOk(WorkspaceRoots.Format(roots)).ToArray());
    }
}
