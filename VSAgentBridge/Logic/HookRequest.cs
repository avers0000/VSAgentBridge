using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// One request to "vsagentbridge hook" in the bridge's own, harness-neutral format:
/// {"event":"before","tool":"edit","path":"...","cwd":"...","harness":"opencode","session":"..."}.
/// Adapters translate harness events into it.
/// </summary>
internal sealed class HookRequest
{
    public const string BeforeEvent = "before";

    public HookRequest(string @event, string tool, string filePath, string? cwd, string? harness, string? session)
    {
        Event = @event;
        Tool = tool;
        FilePath = filePath;
        Cwd = cwd;
        Harness = harness;
        Session = session;
    }

    /// <summary>Only "before" for now.</summary>
    public string Event { get; }

    /// <summary>Harness tool name (edit, write, Edit, ...); logged only at this stage.</summary>
    public string Tool { get; }

    /// <summary>File path as the model gave it: absolute or relative to <see cref="Cwd"/>.</summary>
    public string FilePath { get; }

    /// <summary>Agent's working directory, absolute; null means "the hook process's current directory".</summary>
    public string? Cwd { get; }

    /// <summary>Optional, for the log.</summary>
    public string? Harness { get; }

    /// <summary>Optional, for the log (several agent sessions may run at once).</summary>
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

            if (ev != BeforeEvent)
                error = ev == null ? "'event' is missing or not a string" : $"unsupported event '{ev}' (only '{BeforeEvent}')";
            else if (string.IsNullOrEmpty(tool))
                error = "'tool' is missing or not a string";
            else if (string.IsNullOrWhiteSpace(path))
                error = "'path' is missing or empty";
            else if (cwd != null && !Path.IsPathRooted(cwd))
                error = "'cwd' must be an absolute path";
            else
            {
                error = "";
                return new HookRequest(ev!, tool!, path!, cwd, GetString(root, "harness"), GetString(root, "session"));
            }

            return null;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}