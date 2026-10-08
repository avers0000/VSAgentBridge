using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Hook;

/// <summary>
/// "vsagentbridge hook": reads one JSON request from stdin, writes one JSON response to stdout,
/// logs the call according to vsagentbridge.json.
/// "before": a path that already has a copy in this session → the copy (slice 3); otherwise an existing file →
/// edit logic (slice 1), a missing file → new-file logic (slice 2); whatever cannot be written in place goes to
/// the session folder under Output (slice 3).
/// "after": the new file was written → decide again and add it to its project; if that fails, copy it to Output.
/// "read": a path that has a copy in this session is read from the copy.
/// "shell": a command that deletes, moves or writes files is refused (slice 4); "delete"/"rename" (a patch) likewise.
/// stdout carries only the response; diagnostics go to stderr and the log.
/// Exit code 0 whenever a response was written (allow, deny or redirect); 1 for an invalid request.
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
    private const string ReasonSessionCopy = "SessionCopy";
    private const string ReasonNoSessionCopy = "NoSessionCopy";
    private const string ReasonInOutput = "InOutput";
    private const string ReasonOutputFailed = "OutputFailed";
    private const string ReasonNoFileChange = "NoFileChange";
    private const string ReasonDeleteOrMoveRefused = "DeleteOrMoveRefused";
    private const string ReasonWriteRefused = "WriteRefused";

    // A command can be a whole script; the log keeps its start.
    private const int MaxLoggedCommandChars = 1000;

    public static int Run()
    {
        var total = Stopwatch.StartNew();
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var settings = BridgeSettings.Load(exeDirectory, out var settingsWarning);
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

            var response = Handle(request, settings.ResolveOutputDir(exeDirectory), log, out importance);
            log.Add("verdict", response.DecisionName);
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
    private static HookResponse Handle(HookRequest request, string outputDir, HookLog log, out LogVerbosity importance)
    {
        // Which internal-error text fits if something throws: the file is being created, or an existing one edited.
        var creating = request.IsAfter;
        try
        {
            var cwd = request.Cwd ?? Environment.CurrentDirectory;

            // Slice 4: no file to check out or redirect; nothing here needs Visual Studio.
            if (request.IsShell)
                return HandleShell(request, cwd, outputDir, log, out importance);
            if (request.IsDelete || request.IsRename)
                return HandleDeleteOrMove(request, cwd, outputDir, log, out importance);

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

            // A copy under Output, used by its real path (the model may take it from a redirect message):
            // written and read as it is, never redirected into _outside of its own session.
            if (OutputLayout.IsInOutput(path, outputDir))
            {
                importance = LogVerbosity.All;
                return HookResponse.Allow(ReasonInOutput);
            }

            // Writes and reads of the same file in the same session always meet at this one path.
            // Paths inside it are relative to the agent's cwd as given; without one, under _outside.
            var target = OutputLayout.TargetPath(outputDir, request.Session, request.Cwd, path);
            var output = new OutputStore(
                OutputLayout.SessionFolder(outputDir, request.Session), request.Session, request.Harness, request.Cwd);

            if (request.IsRead)
                return HandleRead(target, log, out importance);

            using var filter = MessageFilter.Register();

            if (request.IsAfter)
                return HandleAfter(path, target, output, log, out importance);

            // Edit or creation is decided by the disk, not by the tool: "edit" with an empty oldString
            // creates a file too (slice 2 results, §1.2).
            creating = !File.Exists(path);

            // Once a file of this session went to Output, the whole session works on that copy, whatever
            // Visual Studio can do now: otherwise edits of one session would split between the copy and the original.
            if (File.Exists(target))
                return RedirectToSessionCopy(target, creating, log, out importance);

            return creating
                ? HandleNewFileBefore(path, target, output, log, out importance)
                : HandleEdit(path, cwd, target, output, log, out importance);
        }
        catch (Exception ex)
        {
            // A failure of the bridge must reach the model as a clear denial,
            // not as a raw tool error it would try to work around.
            log.Add("exception", ex.ToString());
            importance = LogVerbosity.Issues;

            // Reading changes nothing: a failed lookup of the session copy must not stop the model from reading.
            if (request.IsRead)
                return HookResponse.Allow(ReasonInternalError);

            // A failure of the bridge is critical for commands too (slice 4 results, §1.4); a delete or move
            // would be refused anyway, so its own text tells the model what to do.
            if (request.IsShell)
                return HookResponse.Deny(ReasonInternalError, ShellMessages.InternalError);
            if (request.IsDelete || request.IsRename)
                return HookResponse.Deny(ReasonInternalError,
                    ShellMessages.DeleteOrMovePatch(request.IsDelete ? ShellOperation.Delete : ShellOperation.Move, recordPath: null));

            return HookResponse.Deny(ReasonInternalError,
                creating ? HookMessages.InternalErrorNewFile : HookMessages.InternalError);
        }
    }

    // ---- Shell, delete, rename (slice 4) ----

    // Deletes and moves are refused and recorded for the user; writes are refused with a pointer to the write/edit
    // tools; anything else runs. The command itself is never changed.
    private static HookResponse HandleShell(
        HookRequest request, string cwd, string outputDir, HookLog log, out LogVerbosity importance)
    {
        var command = request.Command!;
        log.Add("command", command.Length > MaxLoggedCommandChars ? command.Substring(0, MaxLoggedCommandChars) + "..." : command);
        log.Add("workdir", request.Workdir);

        var findings = ShellCommandScanner.Scan(command);
        if (findings.Count == 0)
        {
            importance = LogVerbosity.All;
            return HookResponse.Allow(ReasonNoFileChange);
        }

        log.Add("found", string.Join(",", findings.Select(f => $"{f.Operation}:{f.Command}")));
        importance = LogVerbosity.Issues;

        if (!findings.Any(f => f.IsDeleteOrMove))
            return HookResponse.Deny(ReasonWriteRefused, ShellMessages.WriteCommand(findings));

        // Paths in the record are full, from the folder the command runs in; the model sees none of them.
        var baseDir = request.Workdir != null ? ResolvePath(request.Workdir, cwd) : cwd;
        var details = findings.Select(f =>
            $"{ShellMessages.Verb(f.Operation)}: {f.Command}" +
            (f.Paths.Count > 0 ? " " + string.Join(", ", f.Paths.Select(p => ResolvePath(p, baseDir))) : ""));

        var record = Record(request, outputDir, command, details, log);
        return HookResponse.Deny(ReasonDeleteOrMoveRefused, ShellMessages.DeleteOrMoveCommand(findings, record));
    }

    // A patch deletes or moves a file: the whole patch is refused, the operation recorded for the user.
    private static HookResponse HandleDeleteOrMove(
        HookRequest request, string cwd, string outputDir, HookLog log, out LogVerbosity importance)
    {
        var path = ResolvePath(request.FilePath, cwd);
        log.Add("path", path);

        string operation;
        if (request.IsDelete)
        {
            operation = $"delete {path}";
        }
        else
        {
            var newPath = ResolvePath(request.NewPath!, cwd);
            log.Add("newPath", newPath);
            operation = $"move {path} -> {newPath}";
        }

        importance = LogVerbosity.Issues;
        var record = Record(request, outputDir, operation, Array.Empty<string>(), log);
        var kind = request.IsDelete ? ShellOperation.Delete : ShellOperation.Move;
        return HookResponse.Deny(ReasonDeleteOrMoveRefused, ShellMessages.DeleteOrMovePatch(kind, record));
    }

    // The record of the refused request in the session folder; null (and the reason in the log) if it failed.
    private static string? Record(HookRequest request, string outputDir, string what, IEnumerable<string> details, HookLog log)
    {
        var store = new OutputStore(
            OutputLayout.SessionFolder(outputDir, request.Session), request.Session, request.Harness, request.Cwd);

        var error = store.RecordPendingOperation(request.Tool, what, details);
        if (error != null)
        {
            log.Add("pendingError", error);
            return null;
        }

        log.Add("pending", store.PendingOperationsPath);
        return store.PendingOperationsPath;
    }

    // Full path for the record and the log; as written when it is not a path .NET accepts (masks, odd characters).
    // A Git Bash path (/d/work/x.cs) is read as D:\work\x.cs.
    private static string ResolvePath(string path, string baseDir)
    {
        var p = path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == '/'
            ? char.ToUpperInvariant(path[1]) + @":\" + path.Substring(3).Replace('/', '\\')
            : path;

        try
        {
            return PathUtil.ToFullPath(p, baseDir);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return path;
        }
    }

    // ---- Read (slice 3) ----

    private static HookResponse HandleRead(string target, HookLog log, out LogVerbosity importance)
    {
        // No Visual Studio, no TFVC cache: a read only needs to know whether this session has a copy.
        if (!File.Exists(target))
        {
            importance = LogVerbosity.All;
            return HookResponse.Allow(ReasonNoSessionCopy);
        }

        log.Add("output", target);
        importance = LogVerbosity.Actions;
        return HookResponse.Redirect(ReasonSessionCopy, target, RedirectMessages.Read(target));
    }

    // ---- Session copy (slice 3) ----

    // The full instructions were given with the first redirect of this file in the session; here one line suffices
    // (slice 3 results, §4.1). The copy exists, so nothing under Output needs preparing and no disk error can occur.
    private static HookResponse RedirectToSessionCopy(
        string target, bool creating, HookLog log, out LogVerbosity importance)
    {
        log.Add("exists", !creating);
        log.Add("output", target);
        log.Add("outputCopy", "reused"); // as OutputResult.CopyState for a reused copy

        // An expected consequence of an earlier redirect, not a new problem: logged with the actions.
        importance = LogVerbosity.Actions;
        return HookResponse.Redirect(ReasonSessionCopy, target, RedirectMessages.SessionCopy(target));
    }

    // ---- Existing file (slice 1) ----

    private static HookResponse HandleEdit(
        string path, string cwd, string target, OutputStore output, HookLog log, out LogVerbosity importance)
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
                {
                    importance = LogVerbosity.Issues;
                    var (cause, fix) = HookMessages.Explain(decision.Reason);
                    return RedirectExisting(path, target, output, decision.Reason.ToString(), cause, fix, log);
                }

            case EditAction.Checkout:
                // Decision Checkout implies Visual Studio was asked and an instance was selected.
                return CheckOut(probe.SelectedInstance!, path, target, output, log, out importance);

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, "Unknown edit action.");
        }
    }

    private static HookResponse CheckOut(
        VsInstance vs, string path, string target, OutputStore output, HookLog log, out LogVerbosity importance)
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
        var (cause, fix) = HookMessages.ExplainCheckoutFailure(result);
        return RedirectExisting(path, target, output, ReasonCheckoutFailed, cause, fix, log);
    }

    private static HookResponse RedirectExisting(
        string path, string target, OutputStore output, string reason, string cause, string? fix, HookLog log)
    {
        var result = output.PrepareCopy(path, target);
        LogOutput(log, result);

        return result.Succeeded
            ? HookResponse.Redirect(reason, target, RedirectMessages.ExistingFile(target, cause, fix))
            : HookResponse.Deny(ReasonOutputFailed, RedirectMessages.OutputFailed(cause, result.Error!, isNewFile: false));
    }

    // ---- New file (slice 2; the same for any version control since the VCS-independence fix) ----

    private static HookResponse HandleNewFileBefore(
        string path, string target, OutputStore output, HookLog log, out LogVerbosity importance)
    {
        log.Add("exists", false);
        var (decision, _) = DecideNewFile(path, log);

        // The solution already lists a file that is missing on disk (deleted locally, not yet got from source control):
        // creating it anew would bypass the project and source control (slice 2 results, §1.4a).
        var alreadyInProject = decision.Reason == NewFileReason.AlreadyInProject;
        if (decision.Action == NewFileAction.Redirect || alreadyInProject)
        {
            importance = LogVerbosity.Issues;
            var (cause, fix) = NewFileMessages.Explain(decision);
            return RedirectNew(target, output, decision.Reason.ToString(), cause, fix,
                addToProject: !alreadyInProject, decision.Tfvc.IsUnderTfvc, log);
        }

        // The "after" call is the one that does something; this line matters only while debugging.
        importance = LogVerbosity.All;
        return HookResponse.Allow(decision.Reason.ToString(), callAfter: decision.RequestsAfter);
    }

    private static HookResponse RedirectNew(
        string target, OutputStore output, string reason, string cause, string? fix,
        bool addToProject, bool underTfvc, HookLog log)
    {
        var result = output.PrepareNewFile(target);
        LogOutput(log, result);

        return result.Succeeded
            ? HookResponse.Redirect(reason, target, RedirectMessages.NewFile(target, cause, fix, addToProject, underTfvc))
            : HookResponse.Deny(ReasonOutputFailed, RedirectMessages.OutputFailed(cause, result.Error!, isNewFile: true));
    }

    private static HookResponse HandleAfter(
        string path, string target, OutputStore output, HookLog log, out LogVerbosity importance)
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
                return AddToProject(probe.InstanceAt(decision.SolutionIndex)!, decision, path, target, output, log, out importance);

            case NewFileAction.LeaveOutsideProjects:
                importance = LogVerbosity.Issues;
                return HookResponse.Allow(decision.Reason.ToString(), NewFileMessages.OutsideProjects(decision.Tfvc.IsUnderTfvc));

            case NewFileAction.Redirect:
                {
                    // Too late to redirect: the file exists. It stays in place, a copy goes to Output.
                    importance = LogVerbosity.Issues;
                    var (cause, fix) = NewFileMessages.Explain(decision);
                    return CopyNotAdded(path, target, output, decision.Reason.ToString(), cause, fix,
                        decision.Tfvc.IsUnderTfvc, log);
                }

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

    // The file stays where the tool wrote it (Visual Studio may already refer to it there); the copy makes
    // the rest of the session work on one version, which the user later merges back (slice 3 results, §1.5).
    private static HookResponse CopyNotAdded(
        string path, string target, OutputStore output, string reason, string cause, string? fix,
        bool underTfvc, HookLog log)
    {
        var result = output.CopyCreatedFile(path, target);
        LogOutput(log, result);

        var message = RedirectMessages.CreatedNotAdded(
            cause, fix, result.Succeeded ? target : null, result.Error, underTfvc);
        return HookResponse.Allow(reason, message);
    }

    private static (NewFileDecision Decision, VsNewFileProbe Probe) DecideNewFile(string path, HookLog log)
    {
        // The TFVC classification only shapes the texts and the log; Visual Studio is asked for every path.
        var tfvc = ClassifyTfvc(path, log);

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

    private static HookResponse AddToProject(
        VsInstance vs, NewFileDecision decision, string path, string target, OutputStore output,
        HookLog log, out LogVerbosity importance)
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
            return HookResponse.Allow(ReasonAddedToProject, NewFileMessages.AddedToProject(project.Name));
        }

        importance = LogVerbosity.Issues;
        var (cause, fix) = NewFileMessages.ExplainAddFailure(project.Name, result);
        return CopyNotAdded(path, target, output, ReasonAddFailed, cause, fix, decision.Tfvc.IsUnderTfvc, log);
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

    private static void LogOutput(HookLog log, OutputResult result)
    {
        log.Add("output", result.TargetPath);
        log.Add("outputCopy", result.CopyState);
        log.Add("outputError", result.Error);
    }

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
        log.Add("vs", selected != null ? VsInstanceLabel.Format(selected.MonikerVersion, selected.ProcessId) : "none");
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