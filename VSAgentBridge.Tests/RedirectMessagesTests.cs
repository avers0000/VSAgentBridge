using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class RedirectMessagesTests
{
    private const string Copy = @"D:\Tools\VSAB\Output\ses_1\App\X.cs";

    // Slice 3 results, §4.1/§3c: the first text ~550-620 characters plus the copy path (measured 06.10: 539 for an
    // edit, 664 for a new file); repeated texts one short line. A guard against texts growing back unnoticed.
    private const int FirstTextBudget = 560;
    private const int NewFileBudget = 680;
    private const int OneLineBudget = 100;

    [TestMethod]
    public void ExistingFile_SaysWhereAndKeepsTheSessionOnTheCopy()
    {
        var text = RedirectMessages.ExistingFile(Copy, "the cause.", "the fix");

        Assert.IsTrue(text.StartsWith("VS Agent Bridge saved this change to a session copy instead: the cause.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The original is unchanged."), text);
        AssertKeepsWorkingOnCopy(text);
        Assert.IsTrue(text.EndsWith($"tell the user: VS Agent Bridge saved the change to {Copy}; the fix, then merge it into the original by comparing them, not by copying over it.", StringComparison.Ordinal), text);
        AssertNoDelete(text);
        Assert.IsFalse(text.Contains("blocked"), text);
        AssertCopyOnce(text);
    }

    [TestMethod]
    public void ExistingFile_NoFix_NoEmptyClause()
    {
        var text = RedirectMessages.ExistingFile(Copy, "the cause.", fix: null);

        Assert.IsTrue(text.Contains($"{Copy}; merge it into the original"), text);
        Assert.IsFalse(text.Contains(", then"), text);
    }

    [TestMethod]
    public void ExistingFile_RealNotInOpenSolution_FitsTheBudget()
    {
        var (cause, fix) = HookMessages.Explain(EditReason.NotInOpenSolution);

        var text = RedirectMessages.ExistingFile(Copy, cause, fix);

        Assert.IsTrue(text.Length - Copy.Length <= FirstTextBudget, $"{text.Length - Copy.Length}: {text}");
    }

    [TestMethod]
    public void NewFile_AddToProject_SaysAddExistingItemFromTheCopy()
    {
        var text = RedirectMessages.NewFile(Copy, "the cause.", "the fix", addToProject: true, underTfvc: true);

        Assert.IsTrue(text.StartsWith("VS Agent Bridge created this file in a session copy instead: the cause.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("Nothing was created at the original path."), text);
        AssertKeepsWorkingOnCopy(text);
        Assert.IsTrue(text.EndsWith($"tell the user: VS Agent Bridge created the new file as {Copy}; it is in no project and not in TFVC; the fix, then in Solution Explorer right-click the folder where it belongs, Add > Existing Item, and pick that file (Visual Studio copies it there).", StringComparison.Ordinal), text);
        AssertNoDelete(text);
        Assert.IsFalse(text.Contains("blocked"), text);
        AssertCopyOnce(text);
    }

    [TestMethod]
    public void NewFile_AlreadyInProject_SaysMerge()
    {
        var text = RedirectMessages.NewFile(Copy, "the cause.", "the fix", addToProject: false, underTfvc: true);

        Assert.IsTrue(text.EndsWith("the fix, then merge it into the original by comparing them, not by copying over it.", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("Add > Existing Item"), text);
        AssertNoDelete(text);
    }

    [TestMethod]
    public void NewFile_RealNotUnderOpenSolution_FitsTheBudget()
    {
        var decision = new NewFileDecision(NewFileAction.Redirect, NewFileReason.NotUnderOpenSolution,
            new TfvcPathClassification(TfvcPathStatus.ServerWorkspace, null, null, null),
            solutions: null, instances: Array.Empty<string>(), project: null, projectsInFolder: null);
        var (cause, fix) = NewFileMessages.Explain(decision);

        var text = RedirectMessages.NewFile(Copy, cause, fix, addToProject: true, underTfvc: true);

        Assert.IsTrue(text.Length - Copy.Length <= NewFileBudget, $"{text.Length - Copy.Length}: {text}");
    }

    [TestMethod]
    public void SessionCopy_IsOneShortLine()
    {
        var text = RedirectMessages.SessionCopy(Copy);

        Assert.AreEqual($"VS Agent Bridge: saved to the session copy {Copy}, not to the original.", text);
        Assert.IsTrue(text.Length - Copy.Length <= OneLineBudget, text);
    }

    [TestMethod]
    public void Read_IsOneShortLine()
    {
        var text = RedirectMessages.Read(Copy);

        Assert.AreEqual($"VS Agent Bridge: read from the session copy {Copy}, not from the original.", text);
        Assert.IsTrue(text.Length - Copy.Length <= OneLineBudget, text);
    }

    [TestMethod]
    public void CreatedNotAdded_WithCopy_KeepsTheSessionOnTheCopy()
    {
        var text = RedirectMessages.CreatedNotAdded("the cause.", "the fix", Copy, copyError: null, underTfvc: true);

        Assert.IsTrue(text.StartsWith("VS Agent Bridge: the file was created but not added to a project: the cause.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The bridge also saved a session copy of it."), text);
        AssertKeepsWorkingOnCopy(text);
        Assert.IsTrue(text.Contains("do not create it again"), text);
        Assert.IsTrue(text.EndsWith($"tell the user: the new file is not in a project; VS Agent Bridge keeps its latest version in {Copy}; the fix, then add it in Visual Studio (Add > Existing Item) and merge the copy into it by comparing them.", StringComparison.Ordinal), text);
        AssertNoDelete(text);
        AssertCopyOnce(text);
    }

    [TestMethod]
    public void CreatedNotAdded_CopyFailed_NamesErrorAndDoesNotPromiseACopy()
    {
        var text = RedirectMessages.CreatedNotAdded("the cause.", fix: null, copy: null, copyError: "IOException: disk full", underTfvc: true);

        Assert.IsTrue(text.Contains("IOException: disk full"), text);
        Assert.IsTrue(text.Contains("add it in Visual Studio (Add > Existing Item)."), text);
        Assert.IsFalse(text.Contains("go to the copy"), text);
        Assert.IsFalse(text.Contains(", then"), text);
        AssertNoDelete(text);
    }

    // ---- VCS independence: TFVC is mentioned only when the path is under TFVC ----

    [TestMethod]
    [DataRow(true, "in no project and not in TFVC")]
    [DataRow(false, "in no project")]
    public void InNoProject_DependsOnTfvc(bool underTfvc, string expected)
    {
        Assert.AreEqual(expected, RedirectMessages.InNoProject(underTfvc));
    }

    [TestMethod]
    public void NewFile_NotUnderTfvc_DoesNotMentionTfvc()
    {
        var text = RedirectMessages.NewFile(Copy, "the cause.", "the fix", addToProject: true, underTfvc: false);

        Assert.IsTrue(text.Contains($"created the new file as {Copy}; it is in no project; the fix, then in Solution Explorer"), text);
        Assert.IsFalse(text.Contains("TFVC"), text);
    }

    [TestMethod]
    public void CreatedNotAdded_UnderTfvc_SaysNotInTfvc()
    {
        var text = RedirectMessages.CreatedNotAdded("the cause.", "the fix", Copy, copyError: null, underTfvc: true);

        Assert.IsTrue(text.Contains("It is in no project and not in TFVC; do not create it again"), text);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CreatedNotAdded_NotUnderTfvc_DoesNotMentionTfvc(bool withCopy)
    {
        var text = withCopy
            ? RedirectMessages.CreatedNotAdded("the cause.", "the fix", Copy, copyError: null, underTfvc: false)
            : RedirectMessages.CreatedNotAdded("the cause.", "the fix", copy: null, copyError: "IOException: x", underTfvc: false);

        Assert.IsTrue(text.Contains("It is in no project; do not create it again"), text);
        Assert.IsFalse(text.Contains("TFVC"), text);
    }

    // A real new-file text outside TFVC: no cause, fix or tail mentions TFVC or TFS.
    [TestMethod]
    [DataRow(NewFileReason.NotUnderOpenSolution)]
    [DataRow(NewFileReason.AlreadyInProject)]
    public void NewFile_RealTextNotUnderTfvc_NoTfvcOrTfs(NewFileReason reason)
    {
        var decision = new NewFileDecision(NewFileAction.Redirect, reason, TfvcPathClassification.NotMapped,
            solutions: null, instances: Array.Empty<string>(), project: null, projectsInFolder: null);
        var (cause, fix) = NewFileMessages.Explain(decision);

        var text = RedirectMessages.NewFile(Copy, cause, fix,
            addToProject: reason != NewFileReason.AlreadyInProject, decision.Tfvc.IsUnderTfvc);

        Assert.IsFalse(text.Contains("TFVC") || text.Contains("TFS"), text);
    }

    [TestMethod]
    public void OutputFailed_Edit_IsADenialOfTheEdit()
    {
        var text = RedirectMessages.OutputFailed("the cause.", "IOException: x", isNewFile: false);

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked this edit: the cause.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The file was not changed."), text);
        Assert.IsTrue(text.Contains("Do not retry with any tool"), text);
        Assert.IsTrue(text.Contains("as a diff"), text);
        Assert.IsTrue(text.Contains("IOException: x"), text);
        Assert.IsTrue(text.Contains("outputDir"), text);
    }

    [TestMethod]
    public void OutputFailed_NewFile_IsADenialOfTheCreation()
    {
        var text = RedirectMessages.OutputFailed("the cause.", "IOException: x", isNewFile: true);

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked creating this file: the cause.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("Do not retry with any tool"), text);
        Assert.IsFalse(text.Contains("was not changed"), text);
        Assert.IsFalse(text.Contains("as a diff"), text);
    }

    private static void AssertKeepsWorkingOnCopy(string text)
    {
        Assert.IsTrue(text.Contains("Keep using the original path: this session's reads and edits of it go to the copy;"), text);
        Assert.IsTrue(text.Contains("do not write it anywhere else with any tool."), text);
    }

    // Slice 3 results, §3c: cleaning up Output is the user's business; the model does not ask for it.
    private static void AssertNoDelete(string text) =>
        Assert.IsTrue(text.IndexOf("delete", StringComparison.OrdinalIgnoreCase) < 0, text);

    // §4.1, principle 1: the full copy path appears once.
    private static void AssertCopyOnce(string text)
    {
        var first = text.IndexOf(Copy, StringComparison.Ordinal);
        Assert.IsTrue(first >= 0, text);
        Assert.AreEqual(-1, text.IndexOf(Copy, first + 1, StringComparison.Ordinal), text);
    }
}
