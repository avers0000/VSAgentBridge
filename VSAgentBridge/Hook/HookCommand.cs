using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Hook;

/// <summary>
/// "vsagentbridge hook": reads one JSON request from stdin, writes one JSON response to stdout,
/// logs the call according to vsagentbridge.json.
/// "before": an existing file → edit logic (slice 1); a missing file → new-file logic (slice 2).
/// "after": the new file was written → decide again and add it to its project through Visual Studio.
/// stdout carries only the response; diagnostics go to stderr and the log.
/// Exit code 0 whenever a response was written (allow or deny); 1 for an invalid request.
/// </summary>
internal static class HookCommand
{
    private const int ExitOk = 0;

    // Not 2: Claude Code reads exit code 2 from a hook as "block the tool call".
    // A malformed request is a configuration error, not a decision.
    private const int ExitInvalidRequest = 1;

    private const int MaxLoggedInputChars = 500;

    // Response reasons that are not EditReason / NewFileReason names.
    private const string ReasonCheckedOut = "CheckedOut";
    private const string ReasonCheckoutFailed = "CheckoutFailed";
    private const string ReasonAddedToProject = "AddedToProject";
    private const string ReasonAddFailed = "AddFailed";
    private const string ReasonNotCreated = "NotCreated";
    private const string ReasonInternalError = "InternalError";
    private const string ReasonInvalidPath = "InvalidPath";

    public static int Run()
    {
        var total = Stopwatch.StartNew();
        var settings = BridgeSettings.Load(AppDomain.CurrentDomain.BaseDirectory, out var settingsWarning);
        var log = new HookLog();
        log.Add("settingsWarning", settingsWarning);

        // Until a response exists, the call is an issue (invalid request or a crash).
        var importance = LogVerbosity.Issues;
        try
        {
            if (!Console.IsInputRedirected)
                return InvalidRequest(log, "expected a JSON request on stdin", input: null);

            var input = ReadStdin();
            var request = HookRequest.Parse(input, out var error);
            if (request == null)
                return InvalidRequest(log, error, input);

            log.Add("harness", request.Harness);
            log.Add("session", request.Session);
            log.Add("event", request.Event);
            log.Add("tool", request.Tool);

            var response = Handle(request, log, out importance);
            log.Add("verdict", response.Allowed ? "allow" : "deny");
            log.Add("result", response.Reason);
            if (response.CallAfter)
                log.Add("callAfter", true);

            WriteStdout(response.ToJson());
            return ExitOk;
        }
        finally
        {
            log.Add("totalMs", total.ElapsedMilliseconds);
            if (settingsWarning != null && importance > LogVerbosity.Issues)
                importance = LogVerbosity.Issues;
            log.Write(settings, importance);
        }
    }

    /// <param name="importance">The least verbose log level that still records this call.</param>
    private static HookResponse Handle(HookRequest request, HookLog log, out LogVerbosity importance)
    {
        try
        {
            var cwd = request.Cwd ?? Environment.CurrentDirectory;

            string path;
            try
            {
                path = PathUtil.ToFullPath(request.FilePath, cwd);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                // Not a path the bridge can reason about; the tool itself will report it.
                log.Add("path", request.FilePath);
                log.Add("pathError", ex.Message);
                importance = LogVerbosity.Issues;
                return HookResponse.Allow(ReasonInvalidPath);
            }

            log.Add("path", path);

            using var filter = MessageFilter.Register();

            if (request.IsAfter)
                return HandleAfter(path, log, out importance);

            // Edit or creation is decided by the disk, not by the tool: "edit" with an empty oldString
            // creates a file too (slice 2 results, §1.2).
            return File.Exists(path)
                ? HandleEdit(path, cwd, log, out importance)
                : HandleNewFileBefore(path, log, out importance);
        }
        catch (Exception ex)
        {
            // A failure of the bridge must reach the model as a clear denial,
            // not as a raw tool error it would try to work around.
            log.Add("exception", ex.ToString());
            importance = LogVerbosity.Issues;
            return HookResponse.Deny(ReasonInternalError, HookMessages.InternalError);
        }
    }

    // ---- Existing file (slice 1) ----

    private static HookResponse HandleEdit(string path, string cwd, HookLog log, out LogVerbosity importance)
    {
        var readOnly = (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        log.Add("exists", true);
        log.Add("readOnly", readOnly);

        var tfvc = ClassifyTfvc(path, log);

        // Visual Studio is looked up only if the decision needs it (read-only file in a server workspace):
        // selecting the instance takes up to ~1 s while Visual Studio is busy (slice 1 results, §3b).
        var probe = new VsFileProbe(path, cwd);
        var decision = EditDecider.Decide(exists: true, readOnly, tfvc, probe.Query);
        log.Add("action", decision.Action);
        log.Add("reason", decision.Reason);
        AddVisualStudio(log, probe, decision.Vs);

        switch (decision.Action)
        {
            case EditAction.None:
                importance = LogVerbosity.All;
                return HookResponse.Allow(decision.Reason.ToString());

            case EditAction.Redirect:
                // Redirection to Output comes in slice 3; until then the edit is denied with an explanation.
                importance = LogVerbosity.Issues;
                return HookResponse.Deny(decision.Reason.ToString(), HookMessages.ForRedirect(decision.Reason));

            case EditAction.Checkout:
                // Decision Checkout implies Visual Studio was asked and an instance was selected.
                return CheckOut(probe.SelectedInstance!, path, log, out importance);

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, "Unknown edit action.");
        }
    }

    private static HookResponse CheckOut(VsInstance vs, string path, HookLog log, out LogVerbosity importance)
    {
        var sw = Stopwatch.StartNew();
        var result = VsCheckout.CheckOut(vs, path);

        log.Add("checkout", result.Succeeded ? "ok" : "failed");
        log.Add("checkoutMs", sw.ElapsedMilliseconds);
        log.Add("checkOutItem", result.CheckOutItemReturned);
        log.Add("attempts", result.Attempts);
        log.Add("afterWritable", result.IsWritable);
        log.Add("afterCheckedOut", result.IsCheckedOut);
        log.Add("verifyWaitMs", result.VerifyWaitMs);
        log.Add("checkoutError", result.Error);

        if (result.Succeeded)
        {
            importance = LogVerbosity.Actions;
            return HookResponse.Allow(ReasonCheckedOut);
        }

        importance = LogVerbosity.Issues;
        return HookResponse.Deny(ReasonCheckoutFailed, HookMessages.ForCheckoutFailure(result));
    }

    // ---- New file (slice 2) ----

    private static HookResponse HandleNewFileBefore(string path, HookLog log, out LogVerbosity importance)
    {
        log.Add("exists", false);
        var (decision, _) = DecideNewFile(path, log);

        // The solution already lists a file that is missing on disk (deleted locally, not yet got from TFS):
        // creating it anew would be a change TFVC does not know about (slice 2 results, §1.4a).
        if (decision.Action == NewFileAction.Deny || decision.Reason == NewFileReason.AlreadyInProject)
        {
            importance = LogVerbosity.Issues;
            return HookResponse.Deny(decision.Reason.ToString(), NewFileMessages.ForDenial(decision));
        }

        // The "after" call is the one that does something; this line matters only while debugging.
        importance = LogVerbosity.All;
        return HookResponse.Allow(decision.Reason.ToString(), callAfter: decision.RequestsAfter);
    }

    private static HookResponse HandleAfter(string path, HookLog log, out LogVerbosity importance)
    {
        var exists = File.Exists(path);
        log.Add("exists", exists);
        if (!exists)
        {
            // The adapter calls "after" only for a completed tool; a missing file means something else removed it.
            importance = LogVerbosity.Issues;
            return HookResponse.Allow(ReasonNotCreated);
        }

        // Decided again: the solution may have been closed since "before",
        // and an SDK-style project may already list the file through a wildcard.
        var (decision, probe) = DecideNewFile(path, log);

        switch (decision.Action)
        {
            case NewFileAction.AddToProject:
                return AddToProject(probe.InstanceAt(decision.SolutionIndex)!, decision, path, log, out importance);

            case NewFileAction.LeaveOutsideProjects:
                importance = LogVerbosity.Issues;
                return HookResponse.Allow(decision.Reason.ToString(), NewFileMessages.OutsideProjects(path));

            case NewFileAction.Deny:
                // Too late to block: the file exists. The model is told it is in no project.
                importance = LogVerbosity.Issues;
                return HookResponse.Allow(decision.Reason.ToString(), NewFileMessages.NotAdded(path, decision));

            case NewFileAction.None:
                if (decision.Reason == NewFileReason.AlreadyInProject)
                {
                    // Not yet known whether Visual Studio makes such a file a pending add: recorded to find out.
                    LogSourceControl(log, probe.InstanceAt(decision.SolutionIndex), path);
                    importance = LogVerbosity.Actions;
                }
                else
                {
                    importance = LogVerbosity.All;
                }
                return HookResponse.Allow(decision.Reason.ToString());

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, "Unknown new-file action.");
        }
    }

    private static (NewFileDecision Decision, VsNewFileProbe Probe) DecideNewFile(string path, HookLog log)
    {
        var tfvc = ClassifyTfvc(path, log);

        // Visual Studio is asked only for a path in a TFVC server workspace.
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
            if (errors.Count > 0)
                log.Add("vsErrors", string.Join("; ", errors));
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

    private static HookResponse AddToProject(
        VsInstance vs, NewFileDecision decision, string path, HookLog log, out LogVerbosity importance)
    {
        // AddToProject implies a loaded target project in the working instance.
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

        if (result.Succeeded)
        {
            importance = LogVerbosity.Actions;
            return HookResponse.Allow(ReasonAddedToProject, NewFileMessages.AddedToProject(path, project.Name));
        }

        importance = LogVerbosity.Issues;
        return HookResponse.Allow(ReasonAddFailed, NewFileMessages.AddFailed(path, project.Name, result));
    }

    private static void LogSourceControl(HookLog log, VsInstance? vs, string path)
    {
        if (vs == null)
            return;

        try
        {
            var sourceControl = vs.Dte.SourceControl;
            log.Add("underScc", sourceControl.IsItemUnderSCC(path));
            log.Add("checkedOut", sourceControl.IsItemCheckedOut(path));
        }
        catch (COMException ex)
        {
            log.Add("vsError", $"COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
    }

    // ---- Shared ----

    private static TfvcPathClassification ClassifyTfvc(string path, HookLog log)
    {
        var sw = Stopwatch.StartNew();
        var tfvc = TfvcPathClassifier.Classify(path, Environment.MachineName, TfvcCacheReader.ReadAll());
        log.Add("tfvc", tfvc.Status);
        log.Add("cacheMs", sw.ElapsedMilliseconds);
        return tfvc;
    }

    private static void AddVisualStudio(HookLog log, VsFileProbe probe, VsFileFacts? vs)
    {
        if (vs == null)
        {
            log.Add("vs", "not-asked");
            return;
        }

        var selected = probe.SelectedInstance;
        log.Add("vsInstances", probe.InstanceCount);
        log.Add("vs", selected != null ? $"{selected.MonikerVersion}/pid{selected.ProcessId}" : "none");
        log.Add("vsChoice", probe.Selection?.Reason);
        log.Add("vsError", vs.Error);
        if (vs.InstanceFound)
        {
            log.Add("underScc", vs.IsUnderScc);
            log.Add("checkedOut", vs.IsCheckedOut);
        }
        log.Add("vsMs", probe.QueryMs);
    }

    private static int InvalidRequest(HookLog log, string error, string? input)
    {
        Console.Error.WriteLine($"vsagentbridge hook: {error}");
        log.Add("invalidRequest", error);
        if (input != null)
        {
            var trimmed = input.Trim();
            log.Add("input", trimmed.Length > MaxLoggedInputChars ? trimmed.Substring(0, MaxLoggedInputChars) + "..." : trimmed);
        }
        return ExitInvalidRequest;
    }

    // Raw UTF-8, not Console.In: a redirected console stream would be decoded with the OEM code page (cp866)
    // and Cyrillic in paths would be corrupted. A BOM, if the sender adds one, is skipped.
    private static string ReadStdin()
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteStdout(string json)
    {
        var bytes = Utf8NoBom.GetBytes(json + "\n");
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}