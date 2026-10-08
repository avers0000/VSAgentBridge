namespace VSAgentBridge.Logic;

/// <summary>
/// Decides what to do before the agent creates a file that does not exist yet (slice 2 results, §1.4a):
/// step 1 picks the working Visual Studio instance, step 2 decides inside its solution.
/// The same for any version control (TFVC, git or none): Visual Studio is asked for every path.
/// The TFVC classification does not change the decision; it is kept in it for the log and the texts.
/// Pure logic: Visual Studio is asked through a callback.
/// Called again for the "after" event: the solution may have been closed meanwhile,
/// and an SDK-style project may already have picked the file up.
/// </summary>
internal static class NewFileDecider
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <param name="path">Absolute path of the new file.</param>
    /// <param name="tfvc">Classification of the path against the TFVC workspace cache (for the texts and the log).</param>
    /// <param name="queryVisualStudio">
    /// Reports the open solution of every Visual Studio instance; called exactly once.
    /// The decision refers to the working instance by its index in the returned list.
    /// </param>
    public static NewFileDecision Decide(
        string path,
        TfvcPathClassification tfvc,
        Func<IReadOnlyList<VsSolutionFacts>> queryVisualStudio)
    {
        var solutions = queryVisualStudio();
        var fileFolder = AsFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Step 1: the working instance — the only one whose solution folder contains the path.

        // An instance that did not answer may be the working one, or a second one.
        var failed = solutions.Where(s => s.Error != null).Select(s => s.Instance).ToList();
        if (failed.Count > 0)
            return Redirect(NewFileReason.VisualStudioError, tfvc, solutions, instances: failed);

        var covering = Enumerable.Range(0, solutions.Count)
            .Where(i => solutions[i].IsOpen
                        && fileFolder.StartsWith(AsFolder(Path.GetDirectoryName(solutions[i].SolutionPath!)!),
                            StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (covering.Count == 0)
            return Redirect(NewFileReason.NotUnderOpenSolution, tfvc, solutions);

        if (covering.Count > 1)
        {
            return Redirect(NewFileReason.SeveralInstances, tfvc, solutions,
                instances: covering.Select(i => solutions[i].Instance).ToList());
        }

        // Step 2: inside the solution of the working instance.
        var index = covering[0];
        var solution = solutions[index];

        // Adding a path the solution already lists would create a duplicate item.
        if (solution.ContainsPath)
            return new NewFileDecision(NewFileAction.None, NewFileReason.AlreadyInProject, tfvc, solutions, index);

        var matches = solution.Projects
            .Select(p => (Project: p, Folder: AsFolder(p.Directory)))
            .Where(m => fileFolder.StartsWith(m.Folder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return new NewFileDecision(NewFileAction.LeaveOutsideProjects, NewFileReason.OutsideProjects, tfvc, solutions, index);

        // The nearest (deepest) project folder wins. Matching folders of equal length are the same folder:
        // both are prefixes of fileFolder.
        var deepestLength = matches.Max(m => m.Folder.Length);
        var deepest = matches.Where(m => m.Folder.Length == deepestLength).ToList();

        if (deepest.Count > 1)
        {
            return Redirect(NewFileReason.SeveralProjectsInFolder, tfvc, solutions, index,
                projectsInFolder: deepest.Select(m => m.Project).ToList());
        }

        var target = deepest[0];
        if (!target.Project.IsLoaded)
            return Redirect(NewFileReason.ProjectUnloaded, tfvc, solutions, index, project: target.Project);

        var chain = fileFolder.Substring(target.Folder.Length).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return new NewFileDecision(NewFileAction.AddToProject, NewFileReason.InProjectFolder, tfvc, solutions, index,
            project: target.Project, folderChain: chain);
    }

    private static NewFileDecision Redirect(
        NewFileReason reason,
        TfvcPathClassification tfvc,
        IReadOnlyList<VsSolutionFacts> solutions,
        int solutionIndex = -1,
        IReadOnlyList<string>? instances = null,
        VsProjectFacts? project = null,
        IReadOnlyList<VsProjectFacts>? projectsInFolder = null) =>
        new(NewFileAction.Redirect, reason, tfvc, solutions, solutionIndex, instances, project,
            folderChain: null, projectsInFolder);

    // Full path with exactly one trailing separator, so that StartsWith respects folder boundaries:
    // "D:\Src\App\" does not start with "D:\Src\Ap\".
    private static string AsFolder(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? full
            : full + Path.DirectorySeparatorChar;
    }
}