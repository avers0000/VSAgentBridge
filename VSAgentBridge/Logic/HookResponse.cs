using System.Text;
using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// Response of "vsagentbridge hook":
/// {"decision":"allow","reason":"..."} or {"decision":"deny","reason":"...","message":"..."}.
/// </summary>
internal sealed class HookResponse
{
    private HookResponse(bool allowed, string reason, string? message)
    {
        Allowed = allowed;
        Reason = reason;
        Message = message;
    }

    public static HookResponse Allow(string reason) => new(true, reason, null);

    public static HookResponse Deny(string reason, string message) => new(false, reason, message);

    public bool Allowed { get; }

    /// <summary>Machine-readable cause (EditReason name, CheckedOut, CheckoutFailed, ...); for adapters and logs.</summary>
    public string Reason { get; }

    /// <summary>Text for the model; set only when denied.</summary>
    public string? Message { get; }

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
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}