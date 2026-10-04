using System.Diagnostics;
using System.Text;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Hook;

/// <summary>
/// "vsagentbridge hook": reads one JSON request from stdin, writes one JSON response to stdout,
/// logs the call according to vsagentbridge.json. Slice 1: "before" for an edit of an existing file.
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

    // Response reasons that are not EditReason names.
    private const string ReasonCheckedOut = "CheckedOut";
    private const string ReasonCheckoutFailed = "CheckoutFailed";
    private const string ReasonInternalError = "InternalError";
    private const string ReasonInvalidPath = "InvalidPath";

    public static int Run()
    {
        var total = Stopwatch.StartNew();
        var settings = BridgeSettings.Load(AppDomain.CurrentDomain.BaseDirectory, out var settingsWarning);
        var log = new HookLog();
        log.Add("settingsWarning", settingsWarning);

        HookResponse? response = null;
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

            response = Handle(request, log);
            log.Add("verdict", response.Allowed ? "allow" : "deny");
            log.Add("result", response.Reason);

            WriteStdout(response.ToJson());
            return ExitOk;
        }
        finally
        {
            log.Add("totalMs", total.ElapsedMilliseconds);
            log.Write(settings, Importance(response, settingsWarning != null));
        }
    }

    /// <summary>The least verbose log level that still records this call.</summary>
    private static LogVerbosity Importance(HookResponse? response, bool hasSettingsWarning)
    {
        // No response: invalid request (or a crash before the response).
        if (response == null || !response.Allowed || hasSettingsWarning || response.Reason == ReasonInvalidPath)
            return LogVerbosity.Issues;

        return response.Reason == ReasonCheckedOut ? LogVerbosity.Actions : LogVerbosity.All;
    }

    private static HookResponse Handle(HookRequest request, HookLog log)
    {
        try
        {
            using var filter = MessageFilter.Register();
            return HandleEdit(request, log);
        }
        catch (Exception ex)
        {
            // A failure of the bridge must reach the model as a clear denial,
            // not as a raw tool error it would try to work around.
            log.Add("exception", ex.ToString());
            return HookResponse.Deny(ReasonInternalError, HookMessages.InternalError);
        }
    }

    private static HookResponse HandleEdit(HookRequest request, HookLog log)
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
            return HookResponse.Allow(ReasonInvalidPath);
        }

        log.Add("path", path);

        var exists = File.Exists(path);
        var readOnly = exists && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        log.Add("exists", exists);
        log.Add("readOnly", readOnly);

        var sw = Stopwatch.StartNew();
        var tfvc = TfvcPathClassifier.Classify(path, Environment.MachineName, TfvcCacheReader.ReadAll());
        log.Add("tfvc", tfvc.Status);
        log.Add("cacheMs", sw.ElapsedMilliseconds);

        // Visual Studio is looked up only if the decision needs it (read-only file in a server workspace):
        // selecting the instance takes up to ~1 s while Visual Studio is busy (slice 1 results, §3b).
        var probe = new VsFileProbe(path, cwd);
        var decision = EditDecider.Decide(exists, readOnly, tfvc, probe.Query);
        log.Add("action", decision.Action);
        log.Add("reason", decision.Reason);
        AddVisualStudio(log, probe, decision.Vs);

        switch (decision.Action)
        {
            case EditAction.None:
                return HookResponse.Allow(decision.Reason.ToString());

            case EditAction.Redirect:
                // Redirection to Output comes in slice 3; until then the edit is denied with an explanation.
                return HookResponse.Deny(decision.Reason.ToString(), HookMessages.ForRedirect(decision.Reason));

            case EditAction.Checkout:
                // Decision Checkout implies Visual Studio was asked and an instance was selected.
                return CheckOut(probe.SelectedInstance!, path, log);

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, "Unknown edit action.");
        }
    }

    private static HookResponse CheckOut(VsInstance vs, string path, HookLog log)
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

        return result.Succeeded
            ? HookResponse.Allow(ReasonCheckedOut)
            : HookResponse.Deny(ReasonCheckoutFailed, HookMessages.ForCheckoutFailure(result));
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