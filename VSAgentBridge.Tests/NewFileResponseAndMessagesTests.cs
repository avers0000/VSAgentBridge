using System.Text.Json;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileResponseAndMessagesTests
{
    private const string FilePath = @"D:\Src\App\New.cs";

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

    // ---- NewFileMessages: denials ----

    [TestMethod]
    [DataRow(NewFileReason.NotUnderOpenSolution)]
    [DataRow(NewFileReason.SeveralInstances)]
    [DataRow(NewFileReason.VisualStudioError)]
    [DataRow(NewFileReason.AlreadyInProject)]
    [DataRow(NewFileReason.SeveralProjectsInFolder)]
    [DataRow(NewFileReason.ProjectUnloaded)]
    public void ForDenial_EveryDenialReason_HasSpecificText(NewFileReason reason)
    {
        var text = NewFileMessages.ForDenial(Decision(reason));

        AssertDenialShape(text);
        Assert.IsFalse(text.Contains("could not be prepared"), $"Fell back to the generic text for {reason}");
    }

    [TestMethod]
    public void ForDenial_AllTextsDiffer()
    {
        var reasons = new[]
        {
            NewFileReason.NotUnderOpenSolution, NewFileReason.SeveralInstances, NewFileReason.VisualStudioError,
            NewFileReason.AlreadyInProject, NewFileReason.SeveralProjectsInFolder, NewFileReason.ProjectUnloaded,
        };

        var texts = reasons.Select(r => NewFileMessages.ForDenial(Decision(r))).ToList();

        Assert.AreEqual(texts.Count, texts.Distinct().Count());
    }

    [TestMethod]
    public void ForDenial_SeveralInstances_NamesThem()
    {
        var text = NewFileMessages.ForDenial(Decision(NewFileReason.SeveralInstances, instances: new[] { "16.0/pid1", "17.0/pid2" }));

        Assert.IsTrue(text.Contains("16.0/pid1, 17.0/pid2"), text);
    }

    [TestMethod]
    public void ForDenial_SeveralProjectsInFolder_NamesThem()
    {
        var text = NewFileMessages.ForDenial(Decision(NewFileReason.SeveralProjectsInFolder,
            projectsInFolder: new[] { Project("A"), Project("B") }));

        Assert.IsTrue(text.Contains("A, B"), text);
    }

    [TestMethod]
    public void ForDenial_ProjectUnloaded_NamesProject()
    {
        var text = NewFileMessages.ForDenial(Decision(NewFileReason.ProjectUnloaded, project: Project("Db")));

        Assert.IsTrue(text.Contains("'Db'"), text);
    }

    [TestMethod]
    public void ForDenial_NotUnderOpenSolution_MentionsElevation()
    {
        Assert.IsTrue(NewFileMessages.ForDenial(Decision(NewFileReason.NotUnderOpenSolution)).Contains("elevation level"));
    }

    [TestMethod]
    public void ForDenial_UnexpectedReason_GenericText()
    {
        var text = NewFileMessages.ForDenial(Decision(NewFileReason.InProjectFolder));

        AssertDenialShape(text);
        Assert.IsTrue(text.Contains("could not be prepared (InProjectFolder)"), text);
    }

    // ---- NewFileMessages: after the tool ran ----

    [TestMethod]
    public void AddedToProject_NamesPathAndProject()
    {
        var text = NewFileMessages.AddedToProject(FilePath, "App");

        Assert.IsTrue(text.Contains(FilePath), text);
        Assert.IsTrue(text.Contains("'App'"), text);
        Assert.IsTrue(text.Contains("Do not edit the project file"), text);
    }

    [TestMethod]
    public void OutsideProjects_SaysCreatedButNotInTfvc()
    {
        var text = NewFileMessages.OutsideProjects(FilePath);

        AssertCreatedNotAddedShape(text);
    }

    [TestMethod]
    public void NotAdded_UsesTheCauseOfTheDecision()
    {
        var text = NewFileMessages.NotAdded(FilePath, Decision(NewFileReason.ProjectUnloaded, project: Project("Db")));

        AssertCreatedNotAddedShape(text);
        Assert.IsTrue(text.Contains("project 'Db' is unloaded"), text);
        Assert.IsTrue(text.Contains("Do not create it again"), text);
    }

    [TestMethod]
    public void AddFailed_Error_IncludesError()
    {
        var text = NewFileMessages.AddFailed(FilePath, "App", Result(error: "COMException 0x80004005: boom"));

        AssertCreatedNotAddedShape(text);
        Assert.IsTrue(text.Contains("COMException 0x80004005: boom"), text);
    }

    [TestMethod]
    public void AddFailed_Copy_NamesTheCopy()
    {
        var text = NewFileMessages.AddFailed(FilePath, "App", Result(addedItemPath: @"D:\Src\App\Copy\New.cs"));

        Assert.IsTrue(text.Contains(@"a copy at D:\Src\App\Copy\New.cs"), text);
    }

    [TestMethod]
    public void AddFailed_ItemNotFound_SaysSo()
    {
        var text = NewFileMessages.AddFailed(FilePath, "App", Result(itemFound: false));

        Assert.IsTrue(text.Contains("does not list the file"), text);
    }

    [TestMethod]
    public void AddFailed_NotSaved_SaysSo()
    {
        var text = NewFileMessages.AddFailed(FilePath, "App", Result(projectSaved: false));

        Assert.IsTrue(text.Contains("could not be saved"), text);
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

    private static void AssertDenialShape(string text)
    {
        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked creating this file: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The file was not created."), text);
        Assert.IsTrue(text.Contains("with any tool"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
    }

    private static void AssertCreatedNotAddedShape(string text)
    {
        Assert.IsTrue(text.StartsWith("VS Agent Bridge: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains(FilePath + " was created"), text);
        Assert.IsTrue(text.Contains("not in TFVC"), text);
        Assert.IsTrue(text.Contains("Add > Existing Item"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
    }

    private static NewFileDecision Decision(
        NewFileReason reason,
        IReadOnlyList<string>? instances = null,
        VsProjectFacts? project = null,
        IReadOnlyList<VsProjectFacts>? projectsInFolder = null) =>
        new(NewFileAction.Deny, reason, new TfvcPathClassification(TfvcPathStatus.ServerWorkspace, null, null, null),
            instances: instances ?? new[] { "17.0/pid1" }, project: project ?? Project("App"),
            projectsInFolder: projectsInFolder);

    private static VsProjectFacts Project(string name) =>
        new(name, $@"D:\Src\{name}\{name}.csproj", $@"D:\Src\{name}", isLoaded: true);

    private static AddToProjectResult Result(
        string? addedItemPath = FilePath, bool itemFound = true, bool projectSaved = true, string? error = null) =>
        new(FilePath, foldersAdded: 0, attempts: 1, addedItemPath, itemFound, projectSaved,
            isUnderScc: null, isCheckedOut: null, error);
}
