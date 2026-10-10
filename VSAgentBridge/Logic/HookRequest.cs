using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// One request to "vsagentbridge hook" in the bridge's own, harness-neutral format:
/// {"event":"before","tool":"edit","path":"...","cwd":"...","harness":"opencode","session":"..."}.
/// Adapters translate harness events into it.
/// "before" comes before a tool writes the file; "after" comes after it completed successfully, and only when
/// the response to "before" asked for it ("after": true), with the "state" of that response returned unchanged
/// (slice 6 results, §1.13); "read" comes before a tool reads the file.
/// "shell" comes before a shell command runs ("command", optional "workdir"; no "path").
/// "delete" and "rename" come before a tool deletes or moves a file ("path"; "rename" also "newPath"):
/// a patch's "*** Delete File" and "*** Move to" (slice 4 results, §1.2).
/// </summary>
internal sealed class HookRequest
{
    public const string BeforeEvent = "before";
    public const string AfterEvent = "after";
    public const string ReadEvent = "read";
    public const string ShellEvent = "shell";
    public const string DeleteEvent = "delete";
    public const string RenameEvent = "rename";

    private static readonly string[] Events = { BeforeEvent, AfterEvent, ReadEvent, ShellEvent, DeleteEvent, RenameEvent };

    public HookRequest(string @event, string tool, string filePath, string? cwd, string? harness, string? session,
        string? command = null, string? workdir = null, string? newPath = null, string? state = null)
    {
        Event = @event;
        Tool = tool;
        FilePath = filePath;
        Cwd = cwd;
        Harness = harness;
        Session = session;
        Command = command;
        Workdir = workdir;
        NewPath = newPath;
        State = state;
    }

    /// <summary>One of the event names above.</summary>
    public string Event { get; }

    public bool IsAfter => Event == AfterEvent;

    /// <summary>
    /// The tool only reads the file. The bridge never checks out or adds anything for it:
    /// it only sends the read to the session copy if one exists.
    /// </summary>
    public bool IsRead => Event == ReadEvent;

    /// <summary>A shell command is about to run: <see cref="Command"/> is set, <see cref="FilePath"/> is empty.</summary>
    public bool IsShell => Event == ShellEvent;

    /// <summary>A tool is about to delete <see cref="FilePath"/>.</summary>
    public bool IsDelete => Event == DeleteEvent;

    /// <summary>A tool is about to move or rename <see cref="FilePath"/> to <see cref="NewPath"/>.</summary>
    public bool IsRename => Event == RenameEvent;

    /// <summary>Harness tool name (edit, write, read, shell, Edit, Bash, ...); a log label only: the bridge never interprets it.</summary>
    public string Tool { get; }

    /// <summary>File path as the model gave it: absolute or relative to <see cref="Cwd"/>. Empty for "shell".</summary>
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

    /// <summary>"shell": the command line as the model gave it.</summary>
    public string? Command { get; }

    /// <summary>"shell": the folder the command runs in, if the tool has one (absolute or relative to <see cref="Cwd"/>).</summary>
    public string? Workdir { get; }

    /// <summary>"rename": the new path, as the model gave it.</summary>
    public string? NewPath { get; }

    /// <summary>
    /// "after": the "state" of the "before" response, as the adapter kept it; read with <see cref="WriteState.Parse"/>.
    /// Null if the adapter sent none.
    /// </summary>
    public string? State { get; }

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
            var command = GetString(root, "command");
            var newPath = GetString(root, "newPath");
            var isShell = ev == ShellEvent;

            if (ev == null)
                error = "'event' is missing or not a string";
            else if (Array.IndexOf(Events, ev) < 0)
                error = $"unsupported event '{ev}' (expected '{string.Join("', '", Events)}')";
            else if (string.IsNullOrEmpty(tool))
                error = "'tool' is missing or not a string";
            else if (isShell && string.IsNullOrWhiteSpace(command))
                error = "'command' is missing or empty";
            else if (!isShell && string.IsNullOrWhiteSpace(path))
                error = "'path' is missing or empty";
            else if (ev == RenameEvent && string.IsNullOrWhiteSpace(newPath))
                error = "'newPath' is missing or empty";
            else if (cwd != null && !Path.IsPathRooted(cwd))
                error = "'cwd' must be an absolute path";
            else
            {
                error = "";
                return new HookRequest(ev, tool!, isShell ? "" : path!, cwd, GetString(root, "harness"),
                    GetString(root, "session"), isShell ? command : null, isShell ? GetString(root, "workdir") : null,
                    ev == RenameEvent ? newPath : null, ev == AfterEvent ? GetString(root, "state") : null);
            }

            return null;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}