using System.Runtime.InteropServices;
using EnvDTE;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Asks every Visual Studio instance about its open solution, for a new file at a given path.
/// Produces one VsSolutionFacts per instance, in the same order, so that an index in the decision
/// maps back to the instance. The caller must have a message filter registered on the current STA thread.
/// </summary>
internal static class VsSolutionQuery
{
    // Project.Kind values (EnvDTE constants, stable across VS versions).
    private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
    private const string MiscellaneousFilesKind = "{66A2671D-8FB5-11D2-AA7E-00C04F688DDE}";
    private const string UnloadedProjectKind = "{67294A52-A4F0-11D2-AA88-00C04F688DDE}";

    public static IReadOnlyList<VsSolutionFacts> Query(IReadOnlyList<VsInstance> instances, string path) =>
        instances.Select(vs => QueryOne(vs, path)).ToList();

    public static string Label(VsInstance vs) => $"{vs.MonikerVersion}/pid{vs.ProcessId}";

    /// <summary>
    /// Project.FullName, or null where the project does not implement it: an unloaded project in VS 2019
    /// answers E_NOTIMPL, which the CLR turns into NotImplementedException rather than COMException.
    /// </summary>
    public static string? TryFullName(Project project)
    {
        try
        {
            return project.FullName;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    private static VsSolutionFacts QueryOne(VsInstance vs, string path)
    {
        var label = Label(vs);
        try
        {
            var solution = vs.Dte.Solution;
            var solutionPath = solution.IsOpen ? solution.FullName : null;

            // "Open Folder" mode reports an open solution without a solution file: nothing to add to.
            if (string.IsNullOrEmpty(solutionPath))
                return VsSolutionFacts.NoSolution(label);

            var solutionFolder = Path.GetDirectoryName(solutionPath)!;

            // Projects matter only for the working instance, and only an instance whose solution folder
            // contains the path can be it (step 1). Walking the projects of the others would only cost time.
            if (!PathUtil.IsUnder(path, solutionFolder))
                return new VsSolutionFacts(label, solutionPath, Array.Empty<VsProjectFacts>(), containsPath: false, error: null);

            var projects = new List<VsProjectFacts>();
            foreach (Project project in solution.Projects)
                Collect(project, solutionFolder, projects);

            return new VsSolutionFacts(label, solutionPath, projects, ListsPath(solution, path), error: null);
        }
        catch (COMException ex)
        {
            // Typical: RPC_E_CALL_REJECTED after the filter gave up (VS busy),
            // RPC_S_SERVER_UNAVAILABLE 0x800706BA (VS closed since the ROT scan).
            return VsSolutionFacts.Failed(label, $"COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
    }

    private static void Collect(Project project, string solutionFolder, List<VsProjectFacts> result)
    {
        var kind = project.Kind;

        // A solution folder is a container: its projects hang below its items as SubProject.
        if (string.Equals(kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
        {
            foreach (ProjectItem item in project.ProjectItems)
            {
                var nested = item.SubProject;
                if (nested != null)
                    Collect(nested, solutionFolder, result);
            }
            return;
        }

        // Holds files opened in the editor that belong to no project; has no folder.
        if (string.Equals(kind, MiscellaneousFilesKind, StringComparison.OrdinalIgnoreCase))
            return;

        // An unloaded project is a placeholder whose FullName is empty or not implemented; UniqueName is always set:
        // the project file's path relative to the solution folder (or absolute when on another drive).
        var fullName = TryFullName(project);
        if (fullName == null || fullName.Length == 0)
        {
            var uniqueName = project.UniqueName;
            if (string.IsNullOrEmpty(uniqueName) || uniqueName.Contains("://"))
                return; // no local path (e.g. a web site project served over HTTP): cannot contain a local file
            fullName = Path.GetFullPath(Path.Combine(solutionFolder, uniqueName));
        }

        // Web site projects have no project file: FullName is the folder itself, with a trailing separator.
        var isFolder = fullName.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
        var directory = isFolder ? fullName.TrimEnd(Path.DirectorySeparatorChar) : Path.GetDirectoryName(fullName)!;

        var isLoaded = !string.Equals(kind, UnloadedProjectKind, StringComparison.OrdinalIgnoreCase);
        result.Add(new VsProjectFacts(project.Name, fullName, directory, isLoaded));
    }

    // FindProjectItem also finds files opened in the editor that belong to no project (Miscellaneous Files):
    // such a hit does not mean that a project lists the path.
    private static bool ListsPath(Solution solution, string path)
    {
        var item = solution.FindProjectItem(path);
        if (item == null)
            return false;

        var container = item.ContainingProject;
        return container == null
               || !string.Equals(container.Kind, MiscellaneousFilesKind, StringComparison.OrdinalIgnoreCase);
    }
}