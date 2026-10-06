using System.Text.Json;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileResponseAndMessagesTests
{
    private const string FilePath = @"D:\Src\App\New.cs";

    private static readonly NewFileReason[] RedirectReasons =
    {
        NewFileReason.NotUnderOpenSolution, NewFileReason.SeveralInstances, NewFileReason.VisualStudioError,
        NewFileReason.AlreadyInProject, NewFileReason.SeveralProjectsInFolder, NewFileReason.ProjectUnloaded,
    };

    // ---- HookResponse: "after" and messages with allow ----

    [TestMethod]
    public void ToJson_AllowWithCallAfter_HasAfterTrue()
    {
        var json = HookResponse.Allow("InProjectFolder", callAfter: true).ToJson();

        Assert.AreEqual("""{"decision":"allow","reason":"InProjectFolder","after":true}""", json);
    }

    [TestMethod]
    public void ToJson_AllowWithMessage_HasMessage()
    {
        var json = HookResponse.Allow("AddedToProject", "added").ToJson();

        Assert.AreEqual("""{"decision":"allow","reason":"AddedToProject","message":"added"}""", json);
    }

    [TestMethod]
    public void ToJson_AllowWithoutExtras_UnchangedFromSlice1()
    {
        Assert.AreEqual("""{"decision":"allow","reason":"Writable"}""", HookResponse.Allow("Writable").ToJson());
    }

    [TestMethod]
    public void Deny_NeverAsksForAfter()
    {
        var response = HookResponse.Deny("ProjectUnloaded", "text");

        Assert.IsFalse(response.CallAfter);
        using var doc = JsonDocument.Parse(response.ToJson());
        Assert.IsFalse(doc.RootElement.TryGetProperty("after", out _));
    }

    // ---- NewFileMessages.Explain ----

    [TestMethod]
    [DataRow(NewFileReason.NotUnderOpenSolution)]
    [DataRow(NewFileReason.SeveralInstances)]
    [DataRow(NewFileReason.VisualStudioError)]
    [DataRow(NewFileReason.AlreadyInProject)]
    [DataRow(NewFileReason.SeveralProjectsInFolder)]
    [DataRow(NewFileReason.ProjectUnloaded)]
    public void Explain_EveryRedirectReason_HasSpecificCauseAndFix(NewFileReason reason)
    {
        var (cause, fix) = NewFileMessages.Explain(Decision(reason));

        Assert.IsFalse(cause.Contains("could not be prepared"), $"Fell back to the generic text for {reason}");
        Assert.IsFalse(string.IsNullOrEmpty(fix), $"No fix for {reason}");
        Assert.IsFalse(fix!.Contains("repeat"), $"A redirect needs no repeat: {fix}");
    }

    // Slice 3 results, §4.1: a fix is what the user does before putting the copy in place.
    [TestMethod]
    [DataRow(NewFileReason.NotUnderOpenSolution)]
    [DataRow(NewFileReason.SeveralInstances)]
    [DataRow(NewFileReason.VisualStudioError)]
    [DataRow(NewFileReason.AlreadyInProject)]
    [DataRow(NewFileReason.SeveralProjectsInFolder)]
    [DataRow(NewFileReason.ProjectUnloaded)]
    public void Explain_Fix_IsAnImperativeFragment(NewFileReason reason)
    {
        HookResponseAndMessagesTests.AssertFixFragment(NewFileMessages.Explain(Decision(reason)).Fix!);
    }

    [TestMethod]
    public void ExplainAddFailure_Fixes_AreImperativeFragments()
    {
        HookResponseAndMessagesTests.AssertFixFragment(NewFileMessages.ExplainAddFailure("App", Result(addedItemPath: @"D:\Src\App\Copy\New.cs")).Fix!);
        HookResponseAndMessagesTests.AssertFixFragment(NewFileMessages.ExplainAddFailure("App", Result(projectSaved: false)).Fix!);
    }

    [TestMethod]
    public void Explain_AllCausesDiffer()
    {
        var causes = RedirectReasons.Select(r => NewFileMessages.Explain(Decision(r)).Cause).ToList();

        Assert.AreEqual(causes.Count, causes.Distinct().Count());
    }

    [TestMethod]
    public void Explain_SeveralInstances_NamesProductPidAndSolution()
    {
        var decision = Decision(NewFileReason.SeveralInstances,
            instances: new[] { "16.0/pid1", "17.0/pid2" },
            solutions: new[]
            {
                Solution("16.0/pid1", @"D:\Src\Big.sln"),
                Solution("17.0/pid2", @"D:\Src\Other.sln"),
            });

        var (cause, _) = NewFileMessages.Explain(decision);

        Assert.IsTrue(cause.Contains("VS2019 (pid 1): Big.sln, VS2022 (pid 2): Other.sln"), cause);
    }

    [TestMethod]
    public void Explain_VisualStudioError_NamesInstanceWithoutSolution()
    {
        var decision = Decision(NewFileReason.VisualStudioError,
            instances: new[] { "16.0/pid7" },
            solutions: new[] { VsSolutionFacts.Failed("16.0/pid7", "RPC_E_CALL_REJECTED") });

        var (cause, _) = NewFileMessages.Explain(decision);

        Assert.IsTrue(cause.Contains("(VS2019 (pid 7);"), cause);
        Assert.IsFalse(cause.Contains("pid 7):"), cause);
    }

    [TestMethod]
    public void Explain_SeveralProjectsInFolder_NamesThem()
    {
        var (cause, _) = NewFileMessages.Explain(Decision(NewFileReason.SeveralProjectsInFolder,
            projectsInFolder: new[] { Project("A"), Project("B") }));

        Assert.IsTrue(cause.Contains("A, B"), cause);
    }

    [TestMethod]
    public void Explain_ProjectUnloaded_NamesProjectInCause()
    {
        var (cause, fix) = NewFileMessages.Explain(Decision(NewFileReason.ProjectUnloaded, project: Project("Db")));

        Assert.IsTrue(cause.Contains("'Db'"), cause);
        Assert.AreEqual("reload it", fix);
    }

    [TestMethod]
    public void Explain_NotUnderOpenSolution_FixMentionsElevation()
    {
        Assert.IsTrue(NewFileMessages.Explain(Decision(NewFileReason.NotUnderOpenSolution)).Fix!.Contains("elevation level"));
    }

    [TestMethod]
    public void Explain_AlreadyInProject_FixIsGetLatest()
    {
        Assert.IsTrue(NewFileMessages.Explain(Decision(NewFileReason.AlreadyInProject)).Fix!.Contains("from TFS"));
    }

    [TestMethod]
    public void Explain_UnexpectedReason_GenericCauseNoFix()
    {
        var (cause, fix) = NewFileMessages.Explain(Decision(NewFileReason.InProjectFolder));

        Assert.IsTrue(cause.Contains("could not be prepared (InProjectFolder)"), cause);
        Assert.IsNull(fix);
    }

    // ---- NewFileMessages: after the tool ran ----

    [TestMethod]
    public void AddedToProject_NamesProjectNotPath()
    {
        var text = NewFileMessages.AddedToProject("App");

        Assert.IsTrue(text.Contains("'App'"), text);
        Assert.IsTrue(text.Contains("pending add"), text);
        Assert.IsTrue(text.Contains("do not edit the project file"), text);
        Assert.IsFalse(text.Contains(@":\"), text);
    }

    [TestMethod]
    public void OutsideProjects_SaysNotInTfvcAndHowToAdd()
    {
        var text = NewFileMessages.OutsideProjects;

        Assert.IsTrue(text.StartsWith("VS Agent Bridge: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("outside all projects"), text);
        Assert.IsTrue(text.Contains("not in TFVC"), text);
        Assert.IsTrue(text.Contains("Add > Existing Item"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
    }

    [TestMethod]
    public void ExplainAddFailure_Error_IncludesErrorNoFix()
    {
        var (cause, fix) = NewFileMessages.ExplainAddFailure("App", Result(itemFound: false, error: "COMException 0x80004005: boom"));

        Assert.IsTrue(cause.Contains("COMException 0x80004005: boom"), cause);
        Assert.IsTrue(cause.Contains("'App'"), cause);
        Assert.IsNull(fix);
    }

    [TestMethod]
    public void ExplainAddFailure_Copy_NamesTheCopyAndAsksToRemoveIt()
    {
        var (cause, fix) = NewFileMessages.ExplainAddFailure("App", Result(addedItemPath: @"D:\Src\App\Copy\New.cs"));

        Assert.IsTrue(cause.Contains(@"a copy at D:\Src\App\Copy\New.cs"), cause);
        Assert.IsTrue(fix!.Contains("remove that copy"), fix);
    }

    [TestMethod]
    public void ExplainAddFailure_ItemNotFound_SaysSo()
    {
        var (cause, fix) = NewFileMessages.ExplainAddFailure("App", Result(itemFound: false));

        Assert.IsTrue(cause.Contains("does not list the file"), cause);
        Assert.IsNull(fix);
    }

    [TestMethod]
    public void ExplainAddFailure_NotSaved_SaysSoAndAsksToSave()
    {
        var (cause, fix) = NewFileMessages.ExplainAddFailure("App", Result(projectSaved: false));

        Assert.IsTrue(cause.Contains("could not be saved"), cause);
        Assert.AreEqual("save project 'App' in Visual Studio", fix);
    }

    // ---- AddToProjectResult ----

    [TestMethod]
    public void Result_AllChecksPass_Succeeded()
    {
        var result = Result();

        Assert.IsFalse(result.IsCopy);
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Result_SamePathInOtherCase_IsNotACopy()
    {
        Assert.IsFalse(Result(addedItemPath: FilePath.ToLowerInvariant()).IsCopy);
    }

    [TestMethod]
    public void Result_OtherPath_IsCopyAndNotSucceeded()
    {
        var result = Result(addedItemPath: @"D:\Src\App\Copy\New.cs");

        Assert.IsTrue(result.IsCopy);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void Result_NoAddedItem_IsNotACopy()
    {
        Assert.IsFalse(Result(addedItemPath: null).IsCopy);
    }

    [TestMethod]
    [DataRow(false, true, null, DisplayName = "item not found")]
    [DataRow(true, false, null, DisplayName = "project not saved")]
    [DataRow(true, true, "boom", DisplayName = "error")]
    public void Result_FailedCheck_NotSucceeded(bool itemFound, bool projectSaved, string? error)
    {
        Assert.IsFalse(Result(itemFound: itemFound, projectSaved: projectSaved, error: error).Succeeded);
    }

    [TestMethod]
    public void Result_SccFactsDoNotAffectSuccess()
    {
        var result = new AddToProjectResult(FilePath, 0, 1, FilePath, itemFound: true, projectSaved: true,
            isUnderScc: false, isCheckedOut: false, error: null);

        Assert.IsTrue(result.Succeeded);
    }

    // ---- Helpers ----

    private static NewFileDecision Decision(
        NewFileReason reason,
        IReadOnlyList<string>? instances = null,
        VsProjectFacts? project = null,
        IReadOnlyList<VsProjectFacts>? projectsInFolder = null,
        IReadOnlyList<VsSolutionFacts>? solutions = null) =>
        new(NewFileAction.Redirect, reason, new TfvcPathClassification(TfvcPathStatus.ServerWorkspace, null, null, null),
            solutions: solutions, instances: instances ?? new[] { "17.0/pid1" }, project: project ?? Project("App"),
            projectsInFolder: projectsInFolder);

    private static VsSolutionFacts Solution(string instance, string solutionPath) =>
        new(instance, solutionPath, Array.Empty<VsProjectFacts>(), containsPath: false, error: null);

    private static VsProjectFacts Project(string name) =>
        new(name, $@"D:\Src\{name}\{name}.csproj", $@"D:\Src\{name}", isLoaded: true);

    private static AddToProjectResult Result(
        string? addedItemPath = FilePath, bool itemFound = true, bool projectSaved = true, string? error = null) =>
        new(FilePath, foldersAdded: 0, attempts: 1, addedItemPath, itemFound, projectSaved,
            isUnderScc: null, isCheckedOut: null, error);
}
