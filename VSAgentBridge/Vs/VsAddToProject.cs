using System.Diagnostics;
using System.Runtime.InteropServices;
using EnvDTE;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Adds a new file to a project the way a user does it by hand in Solution Explorer:
/// Add > New Folder for each folder level the project does not have yet, then Add > Existing Item
/// on the folder where the file lies on disk, then Save. Visual Studio does the rest itself
/// (the item in the project file, checkout of the project file, pending add of the file).
/// Verifies the result afterwards. The caller must have a message filter registered on the current STA thread.
/// </summary>
internal static class VsAddToProject
{
    // E_PENDING: "data not yet available" (e.g. SSDT still building its model); the message filter
    // does not retry it. One time budget covers all calls of the operation (slice 2 results, §1.4).
    private const int EPending = unchecked((int)0x8000000A);
    private const int PendingRetryDelayMs = 500;
    private const int PendingBudgetMs = 10_000;

    // EnvDTE kind constants.
    private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
    private const string MiscellaneousFilesKind = "{66A2671D-8FB5-11D2-AA7E-00C04F688DDE}";
    private const string PhysicalFileKind = "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}";

    /// <param name="vs">The working instance.</param>
    /// <param name="projectFilePath">Project file of the target project (VsProjectFacts.FilePath).</param>
    /// <param name="folderChain">Folder names from the project folder down to the file's folder (NewFileDecision.FolderChain).</param>
    /// <param name="filePath">The new file, already written to disk.</param>
    public static AddToProjectResult Add(
        VsInstance vs, string projectFilePath, IReadOnlyList<string> folderChain, string filePath)
    {
        var budget = Stopwatch.StartNew();
        var foldersAdded = 0;
        var attempts = 0;
        string? addedItemPath = null;

        try
        {
            var solution = vs.Dte.Solution;
            var project = FindProject(solution, projectFilePath);
            if (project == null)
                return Failed($"project {projectFilePath} is not loaded in VS {VsSolutionQuery.Label(vs)}");

            // Walk down the chain like expanding folders in Solution Explorer; add the levels that are missing.
            var items = project.ProjectItems;
            foreach (var name in folderChain)
            {
                var parent = items;
                var folder = FindChildFolder(parent, name);
                if (folder == null)
                {
                    folder = Retry(() => parent.AddFolder(name), () => FindChildFolder(parent, name), budget, out _);
                    foldersAdded++;
                }
                items = folder.ProjectItems;
            }

            // Add > Existing Item on the file's own folder: on any other folder SSDT would copy the file there.
            var target = items;
            var added = Retry(() => target.AddFromFile(filePath), () => FindInSolution(solution, filePath), budget, out attempts);
            addedItemPath = added.FileNames[1]; // COM collections start at 1

            // AddFromFile does not save the project (test 29.09).
            Retry(() => { project.Save(); return project; }, () => project.Saved ? project : null, budget, out _);

            var sourceControl = vs.Dte.SourceControl;
            return new AddToProjectResult(
                filePath,
                foldersAdded,
                attempts,
                addedItemPath,
                itemFound: FindInSolution(solution, filePath) != null,
                projectSaved: project.Saved,
                isUnderScc: sourceControl.IsItemUnderSCC(filePath),
                isCheckedOut: sourceControl.IsItemCheckedOut(filePath),
                error: null);
        }
        catch (COMException ex)
        {
            return Failed($"VS {VsSolutionQuery.Label(vs)}: COMException 0x{ex.HResult:X8}: {ex.Message}");
        }

        AddToProjectResult Failed(string error) =>
            new(filePath, foldersAdded, attempts, addedItemPath, itemFound: false, projectSaved: false,
                isUnderScc: null, isCheckedOut: null, error);
    }

    /// <summary>
    /// Runs a Visual Studio operation, retrying E_PENDING within the shared budget. Before each retry,
    /// checks whether the operation went through after all: repeating it would create a duplicate.
    /// </summary>
    private static T Retry<T>(Func<T> operation, Func<T?> findDone, Stopwatch budget, out int attempts)
        where T : class
    {
        attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                return operation();
            }
            catch (COMException ex) when (ex.HResult == EPending && budget.ElapsedMilliseconds < PendingBudgetMs)
            {
                System.Threading.Thread.Sleep(PendingRetryDelayMs);
                var done = findDone();
                if (done != null)
                    return done;
            }
        }
    }

    /// <summary>The loaded project with this project file, also inside solution folders.</summary>
    private static Project? FindProject(Solution solution, string projectFilePath)
    {
        var wanted = Path.GetFullPath(projectFilePath);
        foreach (Project project in solution.Projects)
        {
            var found = FindProject(project, wanted);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Project? FindProject(Project project, string wanted)
    {
        if (string.Equals(project.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
        {
            foreach (ProjectItem item in project.ProjectItems)
            {
                var nested = item.SubProject;
                var found = nested != null ? FindProject(nested, wanted) : null;
                if (found != null)
                    return found;
            }
            return null;
        }

        var fullName = VsSolutionQuery.TryFullName(project); // null for an unloaded project: never the target
        return !string.IsNullOrEmpty(fullName) && string.Equals(Path.GetFullPath(fullName), wanted, StringComparison.OrdinalIgnoreCase)
            ? project
            : null;
    }

    /// <summary>
    /// The folder item with this name directly under <paramref name="items"/>, compared case-insensitively.
    /// Anything but a file counts as a folder: project systems differ in the kind they report for folders.
    /// </summary>
    private static ProjectItem? FindChildFolder(ProjectItems items, string name)
    {
        foreach (ProjectItem item in items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Kind, PhysicalFileKind, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    /// <summary>The project item with this path; a hit in Miscellaneous Files (a file merely open in the editor) does not count.</summary>
    private static ProjectItem? FindInSolution(Solution solution, string path)
    {
        var item = solution.FindProjectItem(path);
        if (item == null)
            return null;

        var container = item.ContainingProject;
        return container != null && string.Equals(container.Kind, MiscellaneousFilesKind, StringComparison.OrdinalIgnoreCase)
            ? null
            : item;
    }
}