using System.Diagnostics;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Hook;

/// <summary>
/// The new-file core shared by the hook ("before"/"after") and the MCP tool create_file (slice 5 results, §1.2):
/// decide where the file goes, bring it to its format, add it to its project through Visual Studio.
/// Each step writes its facts to the log; what to answer the model is up to the caller.
/// Must run on an STA thread with the message filter registered (Visual Studio is called through COM).
/// </summary>
internal static class NewFileFlow
{

    /// <summary>Where the new file goes; Visual Studio is asked for every path, whatever the version control.</summary>
    public static (NewFileDecision Decision, VsNewFileProbe Probe) Decide(string path, HookLog log)
    {
        // The TFVC classification only shapes the texts and the log.
        var sw = Stopwatch.StartNew();
        var tfvc = TfvcPathClassifier.Classify(path);
        log.Add("tfvc", tfvc.Status);
        log.Add("cacheMs", sw.ElapsedMilliseconds);

        var probe = new VsNewFileProbe(path);
        var decision = NewFileDecider.Decide(path, tfvc, probe.Query);

        log.Add("action", decision.Action);
        log.Add("reason", decision.Reason);
        if (decision.Solutions != null)
        {
            log.Add("vsInstances", probe.InstanceCount);
            log.Add("vs", decision.SolutionIndex >= 0 ? decision.Solutions[decision.SolutionIndex].Instance : "none");
            if (decision.SolutionIndex >= 0)
                log.Add("solution", decision.Solutions[decision.SolutionIndex].SolutionPath);
            log.Add("vsMs", probe.QueryMs);

            var errors = decision.Solutions.Where(s => s.Error != null).Select(s => $"{s.Instance}: {s.Error}").ToList();
            if (errors.Count > 0) log.Add("vsErrors", string.Join("; ", errors));
        }
        if (decision.Instances.Count > 0)
            log.Add("instances", string.Join(",", decision.Instances));
        log.Add("project", decision.Project?.FilePath);
        if (decision.FolderChain.Count > 0)
            log.Add("folders", string.Join("\\", decision.FolderChain));
        if (decision.ProjectsInFolder.Count > 0)
            log.Add("projectsInFolder", string.Join(",", decision.ProjectsInFolder.Select(p => p.Name)));

        return (decision, probe);
    }

    /// <summary>
    /// The hook: a file the write tool has just created is brought to its format (in place, keeping its write time).
    /// Never fails the call: a problem is only logged, the file stays as written.
    /// </summary>
    public static void Normalize(string path, NewFileDecision decision, HookLog log)
    {
        var (format, ms, changed, error) = NewFileWriter.Normalize(path, FormatScope(decision, path));
        log.Add("format", format);
        log.Add("formatMs", ms);
        log.Add("formatChanged", changed);
        log.Add("formatError", error);
    }

    /// <summary>create_file: writes the file in its format; null on success, otherwise the error.</summary>
    public static string? Create(string path, string content, NewFileDecision decision, HookLog log)
    {
        var (format, ms, error) = NewFileWriter.Create(path, content, FormatScope(decision, path));
        log.Add("format", format);
        log.Add("formatMs", ms);
        log.Add("writeError", error);
        return error;
    }

    /// <summary>Adds the file to the decision's project in the working instance (decision AddToProject).</summary>
    public static AddToProjectResult Add(VsNewFileProbe probe, NewFileDecision decision, string path, HookLog log)
    {
        // AddToProject implies a loaded target project in the working instance.
        var vs = probe.InstanceAt(decision.SolutionIndex)!;
        var project = decision.Project!;

        var sw = Stopwatch.StartNew();
        var result = VsAddToProject.Add(vs, project.FilePath, decision.FolderChain, path);

        log.Add("add", result.Succeeded ? "ok" : "failed");
        log.Add("addMs", sw.ElapsedMilliseconds);
        log.Add("foldersAdded", result.FoldersAdded);
        log.Add("attempts", result.Attempts);
        if (result.IsCopy)
            log.Add("copy", result.AddedItemPath);
        log.Add("itemFound", result.ItemFound);
        log.Add("projectSaved", result.ProjectSaved);
        log.Add("underScc", result.IsUnderScc);
        log.Add("checkedOut", result.IsCheckedOut);
        log.Add("addError", result.Error);
        return result;
    }

    // Neighbor files are looked for up to the project's folder; outside projects, up to the solution's folder;
    // without either (no working instance), only along the file's own folders.
    private static string FormatScope(NewFileDecision decision, string path)
    {
        if (decision.Project != null)
            return decision.Project.Directory;

        var solution = decision.SolutionIndex >= 0 ? decision.Solutions?[decision.SolutionIndex].SolutionPath : null;
        return solution != null ? Path.GetDirectoryName(solution)! : Path.GetDirectoryName(path)!;
    }
}