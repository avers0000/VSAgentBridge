using System.Text.Json;
using ModelContextProtocol.Protocol;
using VSAgentBridge.Hook;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;
using Outcome = (string Result, string Text, bool IsError, VSAgentBridge.Logic.LogVerbosity Importance);

namespace VSAgentBridge.Mcp;

/// <summary>
/// The MCP tool create_file (slice 5 results, §1.1–§1.3, §1.8): input checks without Visual Studio, then the new-file
/// core shared with the hook (<see cref="NewFileFlow"/>): decide, write in the file's format, add to the project.
/// Only the clean cases are done here; whatever the hook would send to Output is answered "use the write tool", and
/// nothing is created. One log line per call; the work runs on its own STA thread, like one hook process.
/// </summary>
internal static class CreateFileTool
{
    private const string ArgPath = "path";
    private const string ArgContent = "content";

    // Results in the log (the hook's names where the case is the same).
    private const string ResultAddedToProject = "AddedToProject";
    private const string ResultAddFailed = "AddFailed";
    private const string ResultUseWrite = "UseWrite";
    private const string ResultUseEdit = "UseEdit";
    private const string ResultFolderExists = "FolderExists";
    private const string ResultInvalidPath = "InvalidPath";
    private const string ResultInternalError = "InternalError";

    public static Tool Definition { get; } = new()
    {
        Name = McpMessages.CreateFileTool,
        Description = McpMessages.CreateFileDescription,
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = McpMessages.PathDescription },
                content = new { type = "string", description = McpMessages.ContentDescription },
            },
            required = new[] { ArgPath, ArgContent },
        }),
    };

    public static CallToolResult Call(IDictionary<string, JsonElement>? arguments, BridgeSettings settings, string outputDir)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        var log = new HookLog();
        log.Add("mode", "mcp");
        log.Add("tool", McpMessages.CreateFileTool);

        var outcome = RunOnSta(() => Create(arguments, outputDir, log));

        log.Add("result", outcome.Result);
        log.Add("isError", outcome.IsError);
        log.Add("totalMs", total.ElapsedMilliseconds);
        log.Write(settings, outcome.Importance);

        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = outcome.Text } },
            IsError = outcome.IsError,
        };
    }

    private static Outcome Create(IDictionary<string, JsonElement>? arguments, string outputDir, HookLog log)
    {
        var written = false;
        try
        {
            var rawPath = GetString(arguments, ArgPath);
            var content = GetString(arguments, ArgContent);
            if (string.IsNullOrWhiteSpace(rawPath) || content == null)
                return Refused(ResultInvalidPath, McpMessages.InvalidPath($"'{ArgPath}' and '{ArgContent}' are both required"));

            string path;
            try
            {
                // The harness starts the server in the agent's working folder (slice 5 results, §1.3).
                path = PathUtil.ToFullPath(rawPath!, Environment.CurrentDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                log.Add("path", rawPath);
                return Refused(ResultInvalidPath, McpMessages.InvalidPath(ex.Message));
            }
            log.Add("path", path);

            // ---- Input checks, no Visual Studio ----
            if (Directory.Exists(path))
                return Refused(ResultFolderExists, McpMessages.FolderExists);
            if (File.Exists(path))
                return Refused(ResultUseEdit, McpMessages.AlreadyExists);
            if (OutputLayout.IsInOutput(path, outputDir))
                return Refused(ResultUseWrite, McpMessages.UseWrite(McpMessages.InOutput));

            // ---- The shared core ----
            using var filter = MessageFilter.Register();
            var (decision, probe) = NewFileFlow.Decide(path, log);
            log.Add("newFile", "create_file");

            // AlreadyInProject: the hook redirects it in "before" (restore the original first) — the same here.
            var clean = decision.Reason != NewFileReason.AlreadyInProject
                        && (decision.Action == NewFileAction.AddToProject || decision.Action == NewFileAction.LeaveOutsideProjects);
            if (!clean)
            {
                var (cause, _) = NewFileMessages.Explain(decision);
                return Refused(ResultUseWrite, McpMessages.UseWrite(cause));
            }

            var writeError = NewFileFlow.Create(path, content, decision, log);
            if (writeError != null)
                return Refused(ResultUseWrite, McpMessages.UseWrite(McpMessages.WriteFailed(writeError)));
            written = true;

            if (decision.Action == NewFileAction.LeaveOutsideProjects)
                return Done(decision.Reason.ToString(), NewFileMessages.OutsideProjects(decision.Tfvc.IsUnderTfvc), LogVerbosity.Issues);

            var project = decision.Project!;
            var result = NewFileFlow.Add(probe, decision, path, log);
            if (result.Succeeded)
                return Done(ResultAddedToProject, NewFileMessages.AddedToProject(project.Name), LogVerbosity.Actions);

            // Created but not added: the file stays (no session, so no copy); the user adds it by hand.
            var (addCause, addFix) = NewFileMessages.ExplainAddFailure(project.Name, result);
            return Done(ResultAddFailed,
                RedirectMessages.CreatedNotAdded(addCause, addFix, copy: null, copyError: null, decision.Tfvc.IsUnderTfvc),
                LogVerbosity.Issues);
        }
        catch (Exception ex)
        {
            log.Add("exception", ex.ToString());

            // Before writing, the write tool (and the hook) can still do it; after writing, the file exists.
            return written
                ? Done(ResultInternalError,
                    RedirectMessages.CreatedNotAdded(McpMessages.InternalError, fix: null, copy: null, copyError: null, underTfvc: false),
                    LogVerbosity.Issues)
                : Refused(ResultInternalError, McpMessages.UseWrite(McpMessages.InternalError));
        }
    }

    private static string? GetString(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments != null && arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // COM calls to Visual Studio need an STA thread (as Main of the hook process); the SDK calls us on the thread pool.
    private static T RunOnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static Outcome Refused(string result, string text) => (result, text, true, LogVerbosity.Issues);

    private static Outcome Done(string result, string text, LogVerbosity importance) => (result, text, false, importance);
}