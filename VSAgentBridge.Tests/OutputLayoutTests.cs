using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class OutputLayoutTests
{
    private const string Out = @"D:\Tools\VSAB\Output";
    private const string Cwd = @"D:\Src\Executables";

    // ---- Session folder name ----

    [TestMethod]
    [DataRow((string?)null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "blank")]
    [DataRow("...", DisplayName = "dots only")]
    public void SessionFolderName_NoUsableSession_NoSessionFolder(string? session)
    {
        Assert.AreEqual(OutputLayout.NoSessionFolder, OutputLayout.SessionFolderName(session));
    }

    [TestMethod]
    public void SessionFolderName_OpenCodeId_Unchanged()
    {
        Assert.AreEqual("ses_01K6ABCDEFGHJKMNPQRSTVWXYZ", OutputLayout.SessionFolderName("ses_01K6ABCDEFGHJKMNPQRSTVWXYZ"));
    }

    [TestMethod]
    public void SessionFolderName_InvalidCharacters_Replaced()
    {
        Assert.AreEqual("a_b_c_d_e", OutputLayout.SessionFolderName(@"a:b/c\d*e"));
    }

    [TestMethod]
    public void SessionFolderName_TrailingDotsAndSpaces_Removed()
    {
        Assert.AreEqual("ses", OutputLayout.SessionFolderName(" ses. . "));
    }

    [TestMethod]
    public void SessionFolderName_Long_Truncated()
    {
        Assert.AreEqual(64, OutputLayout.SessionFolderName(new string('x', 100)).Length);
    }

    // ---- Path under the session folder ----

    [TestMethod]
    public void RelativePath_InsideCwd_RelativeToCwd()
    {
        Assert.AreEqual(@"App\Implementations\X.cs", OutputLayout.RelativePath(@"D:\Src\Executables\App\Implementations\X.cs", Cwd));
    }

    [TestMethod]
    public void RelativePath_InsideCwd_IgnoresCaseAndTrailingSeparator()
    {
        Assert.AreEqual(@"App\X.cs", OutputLayout.RelativePath(@"d:\src\EXECUTABLES\App\X.cs", Cwd + @"\"));
    }

    [TestMethod]
    public void RelativePath_SiblingWithSamePrefix_IsOutside()
    {
        Assert.AreEqual(@"_outside\D\Src\Executables2\X.cs", OutputLayout.RelativePath(@"D:\Src\Executables2\X.cs", Cwd));
    }

    [TestMethod]
    public void RelativePath_AboveCwd_IsOutsideWithDrive()
    {
        Assert.AreEqual(@"_outside\D\Src\opencode.json", OutputLayout.RelativePath(@"D:\Src\opencode.json", Cwd));
    }

    [TestMethod]
    public void RelativePath_NoCwd_IsOutside()
    {
        Assert.AreEqual(@"_outside\D\Src\Executables\X.cs", OutputLayout.RelativePath(@"D:\Src\Executables\X.cs", cwd: null));
    }

    [TestMethod]
    public void RelativePath_Unc_IsUnderUncFolder()
    {
        Assert.AreEqual(@"_outside\_unc\server\share\dir\X.cs", OutputLayout.RelativePath(@"\\server\share\dir\X.cs", Cwd));
    }

    [TestMethod]
    public void RelativePath_CwdItself_IsOutside()
    {
        Assert.AreEqual(@"_outside\D\Src\Executables", OutputLayout.RelativePath(Cwd, Cwd));
    }

    [TestMethod]
    public void TargetPath_CombinesOutputSessionAndRelativePath()
    {
        var target = OutputLayout.TargetPath(Out, "ses_1", Cwd, @"D:\Src\Executables\App\X.cs");

        Assert.AreEqual(@"D:\Tools\VSAB\Output\ses_1\App\X.cs", target);
    }

    [TestMethod]
    public void TargetPath_SameFileSameSession_SameTarget()
    {
        // Writes and reads of one file in one session must meet at one copy, whatever the spelling of the path.
        var a = OutputLayout.TargetPath(Out, "ses_1", Cwd, @"D:\Src\Executables\App\X.cs");
        var b = OutputLayout.TargetPath(Out, "ses_1", Cwd, @"D:\Src\Executables\App\.\X.cs");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TargetPath_OtherSession_OtherTarget()
    {
        Assert.AreNotEqual(
            OutputLayout.TargetPath(Out, "ses_1", Cwd, @"D:\Src\Executables\X.cs"),
            OutputLayout.TargetPath(Out, "ses_2", Cwd, @"D:\Src\Executables\X.cs"));
    }

    [TestMethod]
    public void TargetPath_NoSession_UnderNoSessionFolder()
    {
        Assert.AreEqual(@"D:\Tools\VSAB\Output\_no-session\X.cs", OutputLayout.TargetPath(Out, null, Cwd, @"D:\Src\Executables\X.cs"));
    }

    // ---- Paths inside Output ----

    [TestMethod]
    public void IsInOutput_CopyPath_True()
    {
        Assert.IsTrue(OutputLayout.IsInOutput(@"D:\Tools\VSAB\Output\ses_1\App\X.cs", Out));
    }

    [TestMethod]
    public void IsInOutput_SiblingFolder_False()
    {
        Assert.IsFalse(OutputLayout.IsInOutput(@"D:\Tools\VSAB\Output2\X.cs", Out));
        Assert.IsFalse(OutputLayout.IsInOutput(@"D:\Src\Executables\X.cs", Out));
    }
}
