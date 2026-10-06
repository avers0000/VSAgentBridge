using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// One request to "vsagentbridge hook" in the bridge's own, harness-neutral format:
/// {"event":"before","tool":"edit","path":"...","cwd":"...","harness":"opencode","session":"..."}.
/// Adapters translate harness events into it.
/// "before" comes before a tool writes the file; "after" comes after it completed successfully, and only when
/// the response to "before" asked for it ("after": true); "read" comes before a tool reads the file.
/// All three carry the same fields.
/// </summary>
internal sealed class HookRequest
{
    public const string BeforeEvent = "before";
    public const string AfterEvent = "after";
    public const string ReadEvent = "read";

    public HookRequest(string @event, string tool, string filePath, string? cwd, string? harness, string? session)
    {
        Event = @event;
        Tool = tool;
        FilePath = filePath;
        Cwd = cwd;
        Harness = harness;
        Session = session;
    }

    /// <summary>"before", "after" or "read".</summary>
    public string Event { get; }

    public bool IsAfter => Event == AfterEvent;

    /// <summary>
    /// The tool only reads the file. The bridge never checks out or adds anything for it:
    /// it only sends the read to the session copy if one exists.
    /// </summary>
    public bool IsRead => Event == ReadEvent;

    /// <summary>Harness tool name (edit, write, read, Edit, ...); a log label only: the bridge never interprets it.</summary>
    public string Tool { get; }

    /// <summary>File path as the model gave it: absolute or relative to <see cref="Cwd"/>.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Agent's working directory, absolute; null means "the hook process's current directory".
    /// Also the base of paths inside the session folder under Output.
    /// </summary>
    public string? Cwd { get; }

    /// <summary>Optional, for the log and for the session file under Output.</summary>
    public string? Harness { get; }

    /// <summary>
    /// Agent session. Names the session folder under Output, so redirected files of different sessions
    /// never mix; also logged (several agent sessions may run at once).
    /// </summary>
    public string? Session { get; }

    /// <summary>Returns the request, or null and the reason if the JSON is not a valid request.</summary>
    public static HookRequest? Parse(string json, out string error)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "the request must be a JSON object";
                return null;
            }

            var ev = GetString(root, "event");
            var tool = GetString(root, "tool");
            var path = GetString(root, "path");
            var cwd = GetString(root, "cwd");

            if (ev == null)
                error = "'event' is missing or not a string";
            else if (ev != BeforeEvent && ev != AfterEvent && ev != ReadEvent)
                error = $"unsupported event '{ev}' (expected '{BeforeEvent}', '{AfterEvent}' or '{ReadEvent}')";
            else if (string.IsNullOrEmpty(tool))
                error = "'tool' is missing or not a string";
            else if (string.IsNullOrWhiteSpace(path))
                error = "'path' is missing or empty";
            else if (cwd != null && !Path.IsPathRooted(cwd))
                error = "'cwd' must be an absolute path";
            else
            {
                error = "";
                return new HookRequest(ev, tool!, path!, cwd, GetString(root, "harness"), GetString(root, "session"));
            }

            return null;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}