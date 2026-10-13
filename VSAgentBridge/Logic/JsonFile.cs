using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSAgentBridge.Logic;

/// <summary>
/// A JSON settings file of an agent (settings.json, opencode.json) that init changes: read as a tree,
/// changed by the caller, written back. Pure string logic: reading and writing the file is up to the caller.
/// Comments are lost when the file is written back; <see cref="HadComments"/> tells the caller to warn.
/// </summary>
internal sealed class JsonFile
{
    // Comments and trailing commas are allowed: these files are often edited by hand (same as vsagentbridge.json).
    // A property name that appears twice in one object is an error: which value the agent uses is unknown,
    // so init must not pick one. JsonNode materializes objects lazily and would not notice it otherwise.
    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    // Same, except that a comment is an error: used only to find out whether the file had comments.
    private static readonly JsonDocumentOptions NoCommentsOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    private JsonFile(JsonObject root, bool hadComments, string newLine, bool finalNewline)
    {
        Root = root;
        HadComments = hadComments;
        NewLine = newLine;
        FinalNewline = finalNewline;
    }

    /// <summary>The root object; the caller changes it in place.</summary>
    public JsonObject Root { get; }

    /// <summary>True if the original text had comments: they are not written back.</summary>
    public bool HadComments { get; }

    /// <summary>Line break of the original text ("\r\n" or "\n"); "\r\n" for a new or one-line file.</summary>
    public string NewLine { get; }

    /// <summary>True if the original text ended with a line break; true for a new or empty file.</summary>
    public bool FinalNewline { get; }

    /// <summary>
    /// Parses <paramref name="text"/>: null, empty or whitespace means a new empty object.
    /// Returns false with a message for the user if the text is not valid JSON or its root is not an object;
    /// the caller must then leave the file untouched.
    /// </summary>
    public static bool TryParse(string? text, out JsonFile? file, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            file = new JsonFile(new JsonObject(), hadComments: false, "\r\n", finalNewline: true);
            error = null;
            return true;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text!, nodeOptions: null, LenientOptions);
        }
        catch (JsonException ex)
        {
            file = null;
            error = $"invalid JSON ({ex.Message})";
            return false;
        }

        if (node is not JsonObject root)
        {
            file = null;
            error = "the root must be a JSON object";
            return false;
        }

        file = new JsonFile(root, HasComments(text!), DetectNewLine(text!), EndsWithNewline(text!));
        error = null;
        return true;
    }

    /// <summary>
    /// The tree as text: indented by 2 spaces, the original line breaks, non-ASCII characters as they are
    /// (not as \u escapes), a final line break if the original had one.
    /// </summary>
    public string Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = NewLine,
            // The default encoder writes "<", "&", "+" and every non-ASCII character as \uXXXX:
            // valid JSON, but it would change values the user typed. Relaxed escaping keeps them readable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        var json = Root.ToJsonString(options);
        return FinalNewline ? json + NewLine : json;
    }

    // The lenient parse has already succeeded, so failing without comment support means there were comments.
    private static bool HasComments(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, NoCommentsOptions);
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string DetectNewLine(string text)
    {
        var index = text.IndexOf('\n');
        if (index < 0)
            return "\r\n";
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }

    private static bool EndsWithNewline(string text) => text.EndsWith("\n", StringComparison.Ordinal);
}