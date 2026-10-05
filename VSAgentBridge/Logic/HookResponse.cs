using System.Text;
using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// Response of "vsagentbridge hook":
/// {"decision":"allow","reason":"..."}, optionally with "message" and/or "after":true,
/// or {"decision":"deny","reason":"...","message":"..."}.
/// </summary>
internal sealed class HookResponse
{
    private HookResponse(bool allowed, string reason, string? message, bool callAfter)
    {
        Allowed = allowed;
        Reason = reason;
        Message = message;
        CallAfter = callAfter;
    }

    /// <param name="reason">Machine-readable cause.</param>
    /// <param name="message">
    /// Text the adapter appends to the tool's result. Meaningful in a response to "after":
    /// before the tool runs there is no result to append it to.
    /// </param>
    /// <param name="callAfter">
    /// Only in a response to "before": call the bridge again with event "after" and the same path
    /// once the tool has completed successfully.
    /// </param>
    public static HookResponse Allow(string reason, string? message = null, bool callAfter = false) =>
        new(true, reason, message, callAfter);

    public static HookResponse Deny(string reason, string message) => new(false, reason, message, callAfter: false);

    public bool Allowed { get; }

    /// <summary>Machine-readable cause (EditReason / NewFileReason name, CheckedOut, AddedToProject, ...); for adapters and logs.</summary>
    public string Reason { get; }

    /// <summary>Text for the model: why the tool was blocked, or what happened after it ran.</summary>
    public string? Message { get; }

    /// <summary>The adapter must send an "after" request when the tool completes successfully.</summary>
    public bool CallAfter { get; }

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        // The default encoder escapes every non-ASCII character as \uXXXX, so the output is pure ASCII
        // and survives any console code page between the bridge and the adapter.
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("decision", Allowed ? "allow" : "deny");
            writer.WriteString("reason", Reason);
            if (Message != null)
                writer.WriteString("message", Message);
            if (CallAfter)
                writer.WriteBoolean("after", true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}