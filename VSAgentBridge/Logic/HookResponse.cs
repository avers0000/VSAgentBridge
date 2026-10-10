using System.Text;
using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// Response of "vsagentbridge hook":
/// {"decision":"allow","reason":"..."}, optionally with "message" and/or "after":true with "state";
/// {"decision":"deny","reason":"...","message":"..."};
/// {"decision":"redirect","reason":"...","path":"...","message":"..."}.
/// </summary>
internal sealed class HookResponse
{
    private HookResponse(
        HookDecision decision, string reason, string? message, bool callAfter, string? redirectPath, WriteState? state = null)
    {
        Decision = decision;
        Reason = reason;
        Message = message;
        CallAfter = callAfter;
        RedirectPath = redirectPath;
        State = state;
    }

    /// <param name="reason">Machine-readable cause.</param>
    /// <param name="message">
    /// Text the adapter appends to the tool's result. Meaningful in a response to "after":
    /// before the tool runs there is no result to append it to.
    /// </param>
    public static HookResponse Allow(string reason, string? message = null) =>
        new(HookDecision.Allow, reason, message, callAfter: false, redirectPath: null);

    /// <summary>
    /// Only in a response to "before": the tool runs as requested; the adapter then calls the bridge again with
    /// event "after", the same path and <paramref name="state"/> returned unchanged (slice 6 results, §1.13).
    /// </summary>
    public static HookResponse AllowThenAfter(string reason, WriteState state) =>
        new(HookDecision.Allow, reason, message: null, callAfter: true, redirectPath: null, state);

    public static HookResponse Deny(string reason, string message) =>
        new(HookDecision.Deny, reason, message, callAfter: false, redirectPath: null);

    /// <param name="path">Absolute path the tool must use instead of the one the model gave.</param>
    /// <param name="message">
    /// Text the adapter appends to the tool's result once the tool has completed successfully;
    /// it tells the model where the file really is. Given already with "before", so that no second call is needed.
    /// </param>
    public static HookResponse Redirect(string reason, string path, string message) =>
        new(HookDecision.Redirect, reason, message, callAfter: false, redirectPath: path);

    public HookDecision Decision { get; }

    /// <summary>The tool runs as requested (the only verdict that leaves the input untouched).</summary>
    public bool Allowed => Decision == HookDecision.Allow;

    /// <summary>Machine-readable cause (EditReason / NewFileReason name, CheckedOut, AddedToProject, ...); for adapters and logs.</summary>
    public string Reason { get; }

    /// <summary>Text for the model: why the tool was blocked, where the file went, or what happened after it ran.</summary>
    public string? Message { get; }

    /// <summary>The adapter must send an "after" request when the tool completes successfully.</summary>
    public bool CallAfter { get; }

    /// <summary>Set only with <see cref="CallAfter"/>: what "before" knew about the file, for "after".</summary>
    public WriteState? State { get; }

    /// <summary>Set only for <see cref="HookDecision.Redirect"/>: where the tool must read or write instead.</summary>
    public string? RedirectPath { get; }

    /// <summary>Verdict as written to JSON and to the log: "allow", "deny" or "redirect".</summary>
    public string DecisionName => Decision switch
    {
        HookDecision.Allow => "allow",
        HookDecision.Deny => "deny",
        HookDecision.Redirect => "redirect",
        _ => throw new ArgumentOutOfRangeException(nameof(Decision), Decision, "Unknown decision."),
    };

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        // The default encoder escapes every non-ASCII character as \uXXXX, so the output is pure ASCII
        // and survives any console code page between the bridge and the adapter.
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("decision", DecisionName);
            writer.WriteString("reason", Reason);
            if (RedirectPath != null)
                writer.WriteString("path", RedirectPath);
            if (Message != null)
                writer.WriteString("message", Message);
            if (CallAfter)
                writer.WriteBoolean("after", true);
            if (State != null)
                writer.WriteString("state", State.ToString());
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}