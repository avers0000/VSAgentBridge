using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// PathUtil.IsUnderAny: the MCP server's "--roots" check (same rule as the mod's isUnderRoots in register.js).
[TestClass]
internal sealed class PathUtilIsUnderAnyTests
{
    [TestMethod]
    [DataRow(@"D:\Projects\App\File.cs", @"D:\Projects")]
    [DataRow(@"D:\Projects\App\File.cs", @"D:\Projects\")]
    [DataRow(@"D:\Projects\App\File.cs", @"d:\projects")]
    [DataRow(@"D:\Projects\App\File.cs", @"D:/Projects")]
    [DataRow(@"D:\Projects", @"D:\Projects")]
    [DataRow(@"D:\Projects\App\File.cs", @"E:\Work;D:\Projects")]
    [DataRow(@"D:\Projects\App\File.cs", @" E:\Work ; D:\Projects ")]
    [DataRow(@"D:\Projects\App\File.cs", @";;D:\Projects;")]
    [DataRow(@"D:\Projects\App\File.cs", @"D:\")]
    public void UnderARoot_True(string path, string roots)
    {
        Assert.IsTrue(PathUtil.IsUnderAny(path, roots));
    }

    [TestMethod]
    [DataRow(@"D:\ProjectsOld\File.cs", @"D:\Projects")]
    [DataRow(@"D:\Other\File.cs", @"D:\Projects;E:\Work")]
    [DataRow(@"E:\Projects\File.cs", @"D:\Projects")]
    [DataRow(@"D:\File.cs", @"D:\Projects")]
    public void OutsideTheRoots_False(string path, string roots)
    {
        Assert.IsFalse(PathUtil.IsUnderAny(path, roots));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(";")]
    [DataRow(" ; ; ")]
    public void NoRoots_NothingIsUnder(string roots)
    {
        Assert.IsFalse(PathUtil.IsUnderAny(@"D:\Projects\App\File.cs", roots));
    }

    [TestMethod]
    [DataRow(@"Projects")]
    [DataRow(@"..\Projects")]
    [DataRow(@"Projects\App")]
    public void RelativeRoot_MatchesNothing(string roots)
    {
        // A relative root would depend on the server's current folder (the session's): it is skipped.
        var current = Environment.CurrentDirectory;
        Assert.IsFalse(PathUtil.IsUnderAny(Path.Combine(current, roots, "File.cs"), roots));
    }

    [TestMethod]
    [DataRow(@"D:\Pro|jects")]
    [DataRow(@"D:\Projects<>")]
    public void InvalidRoot_MatchesNothing_OthersStillWork(string invalid)
    {
        Assert.IsFalse(PathUtil.IsUnderAny(@"D:\Projects\App\File.cs", invalid));
        Assert.IsTrue(PathUtil.IsUnderAny(@"D:\Projects\App\File.cs", invalid + @";D:\Projects"));
    }

    [TestMethod]
    public void UnresolvedPlaceholder_MatchesNothing()
    {
        // Claude Code did not substitute the option: the literal text is not a rooted path.
        Assert.IsFalse(PathUtil.IsUnderAny(@"D:\Projects\App\File.cs", "${user_config.workspaceRoots}"));
    }
}
