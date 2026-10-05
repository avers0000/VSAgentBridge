using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class NewFileDeciderTests
{
    private const string Sln = @"D:\Src\All.sln";
    private const string AppProj = @"D:\Src\App\App.csproj";
    private const string DbProj = @"D:\Src\Db\Db.sqlproj";

    // ---- Without Visual Studio ----

    [TestMethod]
    [DataRow(TfvcPathStatus.NotMapped, NewFileReason.NotTfvc)]
    [DataRow(TfvcPathStatus.LocalWorkspace, NewFileReason.LocalWorkspace)]
    public void Decide_OutsideServerWorkspace_DoesNotAskVisualStudio(TfvcPathStatus status, NewFileReason expectedReason)
    {
        var calls = 0;

        var decision = NewFileDecider.Decide(@"D:\Src\App\New.cs", Classification(status), () =>
        {
            calls++;
            return Array.Empty<VsSolutionFacts>();
        });

        Assert.AreEqual(NewFileAction.None, decision.Action);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(0, calls);
        Assert.IsNull(decision.Solutions);
        Assert.IsFalse(decision.RequestsAfter);
    }

    [TestMethod]
    public void Decide_ServerWorkspace_AsksVisualStudioOnce()
    {
        var calls = 0;
        var solutions = new[] { Solution("17.0/pid1", Sln, Project("App", AppProj)) };

        var decision = NewFileDecider.Decide(@"D:\Src\App\New.cs", Server(), () =>
        {
            calls++;
            return solutions;
        });

        Assert.AreEqual(1, calls);
        Assert.AreSame(solutions, decision.Solutions);
    }

    // ---- Step 1: the working instance ----

    [TestMethod]
    public void Decide_NoInstances_NotUnderOpenSolution()
    {
        var decision = Decide(@"D:\Src\App\New.cs");

        AssertDeny(decision, NewFileReason.NotUnderOpenSolution);
        Assert.AreEqual(-1, decision.SolutionIndex);
    }

    [TestMethod]
    public void Decide_OnlySolutionsElsewhereOrNone_NotUnderOpenSolution()
    {
        var decision = Decide(@"D:\Src\App\New.cs",
            VsSolutionFacts.NoSolution("16.0/pid1"),
            Solution("17.0/pid2", @"D:\Other\Other.sln", Project("App", AppProj)));

        AssertDeny(decision, NewFileReason.NotUnderOpenSolution);
    }

    [TestMethod]
    public void Decide_TwoInstancesCoverThePath_SeveralInstances()
    {
        var decision = Decide(@"D:\Src\App\New.cs",
            Solution("16.0/pid1", @"D:\Src\Big.sln", Project("App", AppProj)),
            Solution("17.0/pid2", @"D:\Src\Small.sln", Project("Db", DbProj)));

        AssertDeny(decision, NewFileReason.SeveralInstances);
        CollectionAssert.AreEqual(new[] { "16.0/pid1", "17.0/pid2" }, decision.Instances.ToArray());
        Assert.AreEqual(-1, decision.SolutionIndex);
    }

    [TestMethod]
    public void Decide_AnInstanceFailed_VisualStudioErrorEvenIfAnotherCovers()
    {
        var decision = Decide(@"D:\Src\App\New.cs",
            Solution("17.0/pid1", Sln, Project("App", AppProj)),
            VsSolutionFacts.Failed("16.0/pid2", "RPC_E_CALL_REJECTED"));

        AssertDeny(decision, NewFileReason.VisualStudioError);
        CollectionAssert.AreEqual(new[] { "16.0/pid2" }, decision.Instances.ToArray());
    }

    [TestMethod]
    public void Decide_WorkingInstanceIsNotFirst_IndexPointsToIt()
    {
        var decision = Decide(@"D:\Src\App\New.cs",
            VsSolutionFacts.NoSolution("16.0/pid1"),
            Solution("17.0/pid2", @"D:\Other\Other.sln"),
            Solution("18.0/pid3", Sln, Project("App", AppProj)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        Assert.AreEqual(2, decision.SolutionIndex);
    }

    [TestMethod]
    public void Decide_SolutionFolderBoundary_PrefixOfNameDoesNotCover()
    {
        // D:\Src\Ap\ must not cover D:\Src\App\.
        var decision = Decide(@"D:\Src\App\New.cs", Solution("17.0/pid1", @"D:\Src\Ap\Ap.sln"));

        AssertDeny(decision, NewFileReason.NotUnderOpenSolution);
    }

    // ---- Step 2: inside the solution ----

    [TestMethod]
    public void Decide_SolutionAlreadyListsPath_AlreadyInProject()
    {
        var decision = Decide(@"D:\Src\App\New.cs",
            Solution("17.0/pid1", Sln, containsPath: true, Project("App", AppProj)));

        Assert.AreEqual(NewFileAction.None, decision.Action);
        Assert.AreEqual(NewFileReason.AlreadyInProject, decision.Reason);
        Assert.AreEqual(0, decision.SolutionIndex);
        Assert.IsFalse(decision.RequestsAfter);
    }

    [TestMethod]
    public void Decide_FileInProjectFolder_AddToProjectWithEmptyChain()
    {
        var decision = Decide(@"D:\Src\App\New.cs", Solution("17.0/pid1", Sln, Project("App", AppProj)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        Assert.AreEqual(NewFileReason.InProjectFolder, decision.Reason);
        Assert.AreEqual(AppProj, decision.Project!.FilePath);
        Assert.AreEqual(0, decision.FolderChain.Count);
        Assert.AreEqual(0, decision.SolutionIndex);
        Assert.IsTrue(decision.RequestsAfter);
    }

    [TestMethod]
    public void Decide_FileInSubfolders_ChainOutermostFirstWithCaseOfThePath()
    {
        var decision = Decide(@"D:\Src\App\Sub\deeper\New.cs", Solution("17.0/pid1", Sln, Project("App", AppProj)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        CollectionAssert.AreEqual(new[] { "Sub", "deeper" }, decision.FolderChain.ToArray());
    }

    [TestMethod]
    public void Decide_ProjectFolderInOtherCase_StillMatches()
    {
        var decision = Decide(@"D:\Src\App\Sub\New.cs",
            Solution("17.0/pid1", Sln, new VsProjectFacts("App", @"d:\src\app\App.csproj", @"d:\src\app", isLoaded: true)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        CollectionAssert.AreEqual(new[] { "Sub" }, decision.FolderChain.ToArray());
    }

    [TestMethod]
    public void Decide_NestedProjects_DeepestWins()
    {
        var decision = Decide(@"D:\Src\App\Tools\Sub\New.cs", Solution("17.0/pid1", Sln,
            Project("App", AppProj),
            Project("Tools", @"D:\Src\App\Tools\Tools.csproj")));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        Assert.AreEqual("Tools", decision.Project!.Name);
        CollectionAssert.AreEqual(new[] { "Sub" }, decision.FolderChain.ToArray());
    }

    [TestMethod]
    public void Decide_NestedProjects_FileInOuterOnly_OuterWins()
    {
        var decision = Decide(@"D:\Src\App\Models\New.cs", Solution("17.0/pid1", Sln,
            Project("App", AppProj),
            Project("Tools", @"D:\Src\App\Tools\Tools.csproj")));

        Assert.AreEqual("App", decision.Project!.Name);
        CollectionAssert.AreEqual(new[] { "Models" }, decision.FolderChain.ToArray());
    }

    [TestMethod]
    public void Decide_ProjectFolderBoundary_PrefixOfNameDoesNotMatch()
    {
        // Project in D:\Src\Ap\ must not take a file from D:\Src\App\.
        var decision = Decide(@"D:\Src\App\New.cs",
            Solution("17.0/pid1", Sln, Project("Ap", @"D:\Src\Ap\Ap.csproj")));

        Assert.AreEqual(NewFileAction.LeaveOutsideProjects, decision.Action);
    }

    [TestMethod]
    public void Decide_SeveralProjectsInOneFolder_Deny()
    {
        var decision = Decide(@"D:\Src\Shared\New.cs", Solution("17.0/pid1", Sln,
            Project("A", @"D:\Src\Shared\A.csproj"),
            Project("B", @"D:\Src\Shared\B.csproj")));

        AssertDeny(decision, NewFileReason.SeveralProjectsInFolder);
        CollectionAssert.AreEqual(new[] { "A", "B" }, decision.ProjectsInFolder.Select(p => p.Name).ToArray());
        Assert.AreEqual(0, decision.SolutionIndex);
    }

    [TestMethod]
    public void Decide_UnloadedProject_Deny()
    {
        var decision = Decide(@"D:\Src\Db\New.sql", Solution("17.0/pid1", Sln,
            new VsProjectFacts("Db", DbProj, @"D:\Src\Db", isLoaded: false)));

        AssertDeny(decision, NewFileReason.ProjectUnloaded);
        Assert.AreEqual("Db", decision.Project!.Name);
    }

    [TestMethod]
    public void Decide_UnderSolutionOutsideProjects_LeaveOutsideProjects()
    {
        var decision = Decide(@"D:\Src\Docs\Notes.md", Solution("17.0/pid1", Sln, Project("App", AppProj)));

        Assert.AreEqual(NewFileAction.LeaveOutsideProjects, decision.Action);
        Assert.AreEqual(NewFileReason.OutsideProjects, decision.Reason);
        Assert.AreEqual(0, decision.SolutionIndex);
        Assert.IsTrue(decision.RequestsAfter);
    }

    [TestMethod]
    public void Decide_InSolutionRootFolder_LeaveOutsideProjects()
    {
        var decision = Decide(@"D:\Src\README.md", Solution("17.0/pid1", Sln, Project("App", AppProj)));

        Assert.AreEqual(NewFileReason.OutsideProjects, decision.Reason);
    }

    [TestMethod]
    public void Decide_SqlProject_WorksLikeAnyOther()
    {
        var decision = Decide(@"D:\Src\Db\dbo\Tables\T.sql", Solution("17.0/pid1", Sln, Project("Db", DbProj)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        CollectionAssert.AreEqual(new[] { "dbo", "Tables" }, decision.FolderChain.ToArray());
    }

    [TestMethod]
    public void Decide_WebSiteProjectWithoutProjectFile_FolderIsTheProject()
    {
        var decision = Decide(@"D:\Src\Site\js\app.js", Solution("17.0/pid1", Sln,
            new VsProjectFacts("Site", @"D:\Src\Site\", @"D:\Src\Site", isLoaded: true)));

        Assert.AreEqual(NewFileAction.AddToProject, decision.Action);
        CollectionAssert.AreEqual(new[] { "js" }, decision.FolderChain.ToArray());
    }

    // ---- Helpers ----

    private static NewFileDecision Decide(string path, params VsSolutionFacts[] solutions) =>
        NewFileDecider.Decide(path, Server(), () => solutions);

    private static void AssertDeny(NewFileDecision decision, NewFileReason reason)
    {
        Assert.AreEqual(NewFileAction.Deny, decision.Action);
        Assert.AreEqual(reason, decision.Reason);
        Assert.IsFalse(decision.RequestsAfter);
    }

    private static VsSolutionFacts Solution(string instance, string solutionPath, params VsProjectFacts[] projects) =>
        Solution(instance, solutionPath, containsPath: false, projects);

    private static VsSolutionFacts Solution(string instance, string solutionPath, bool containsPath, params VsProjectFacts[] projects) =>
        new(instance, solutionPath, projects, containsPath, error: null);

    private static VsProjectFacts Project(string name, string projectFile) =>
        new(name, projectFile, Path.GetDirectoryName(projectFile)!, isLoaded: true);

    private static TfvcPathClassification Server() => Classification(TfvcPathStatus.ServerWorkspace);

    private static TfvcPathClassification Classification(TfvcPathStatus status) =>
        new(status, workspace: null, mappedRoot: null, cacheFilePath: null);
}
