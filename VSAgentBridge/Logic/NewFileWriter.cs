using System.Text;

namespace VSAgentBridge.Logic;

/// <summary>
/// Writes files in their format. New files (slice 5 results, §1.2): <see cref="Create"/> — create_file writes the
/// model's content; <see cref="Normalize"/> — the hook's "after" rewrites what the write tool wrote.
/// Existing files (slice 6 results, §1.13): <see cref="Capture(string)"/> — "before" takes the file's format;
/// <see cref="Restore"/> — "after" brings the rewritten file back to it.
/// A rewrite keeps the file's last write time, so that the harness does not see the file as changed since it wrote it.
/// Always UTF-8; a file that is not valid UTF-8 is never rewritten.
/// </summary>
internal static class NewFileWriter
{
    private const string NotUtf8Error = "not valid UTF-8; left as written";

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>
    /// Creates the file and its missing folders. Never overwrites: an existing file is an error
    /// (the caller checks first; this also covers a file appearing in between).
    /// </summary>
    /// <param name="scopeDirectory">Neighbors are looked for up to this folder (project, or solution).</param>
    public static (NewFileFormat Format, long FormatMs, string? Error) Create(
        string path, string content, string scopeDirectory)
    {
        var format = NewFileFormatResolver.Resolve(path, scopeDirectory, content, out var formatMs);
        var bytes = Encode(Apply(content, format), format.Bom!.Value);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            return (format, formatMs, null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return (format, formatMs, ex.Message);
        }
    }

    /// <summary>
    /// Brings a just-written new file to its format. Not valid UTF-8 → left as it is (Error says why);
    /// already in the format → not rewritten (Changed false).
    /// </summary>
    public static (NewFileFormat? Format, long FormatMs, bool Changed, string? Error) Normalize(
        string path, string scopeDirectory)
    {
        try
        {
            var original = File.ReadAllBytes(path);
            var text = Decode(original);
            if (text == null)
                return (null, 0, false, NotUtf8Error);

            var format = NewFileFormatResolver.Resolve(path, scopeDirectory, text, out var formatMs);
            var bytes = Encode(Apply(text, format), format.Bom!.Value);
            return (format, formatMs, Rewrite(path, original, bytes), null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return (null, 0, false, ex.Message);
        }
    }

    /// <summary>The format of an existing file before a write; null and the error if it cannot be read.</summary>
    public static (WriteState? State, string? Error) Capture(string path)
    {
        try
        {
            return (Capture(File.ReadAllBytes(path)), null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>The format of a file's content (unit tests use it directly).</summary>
    internal static WriteState Capture(byte[] bytes)
    {
        var start = HasBom(bytes) ? Utf8Bom.Length : 0;
        if (!IsUtf8(bytes, start))
            return WriteState.ExistingNotUtf8;

        int crlf = 0, lf = 0, cr = 0;
        for (var i = start; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                if (i > start && bytes[i - 1] == (byte)'\r') crlf++;
                else lf++;
            }
            else if (bytes[i] == (byte)'\r' && (i + 1 == bytes.Length || bytes[i + 1] != (byte)'\n'))
            {
                cr++;
            }
        }

        // One kind only decides; mixed kinds, a lone CR or no line break at all leave line endings as written.
        bool? crlfValue = cr == 0 && (crlf > 0) != (lf > 0) ? crlf > 0 : null;
        var last = bytes.Length > start ? bytes[bytes.Length - 1] : (byte)0;
        return WriteState.Existing(start > 0, crlfValue, finalNewline: last == (byte)'\n' || last == (byte)'\r');
    }

    /// <summary>
    /// Brings an existing file, just rewritten by a tool, back to the format it had before (<paramref name="state"/>
    /// from <see cref="Capture(string)"/>). Was or is not valid UTF-8 → left as it is (Error says why);
    /// already in the format → not rewritten (Changed false).
    /// </summary>
    public static (bool Changed, string? Error) Restore(string path, WriteState state)
    {
        if (state.IsNew)
            throw new ArgumentException("Restore is for an existing file; a new one is normalized.", nameof(state));
        if (state.NotUtf8)
            return (false, "not valid UTF-8 before the write; left as written");

        try
        {
            var current = File.ReadAllBytes(path);
            var text = Decode(current);
            if (text == null)
                return (false, NotUtf8Error);

            var bytes = Encode(ApplyExisting(text, state), state.Bom);
            return (Rewrite(path, current, bytes), null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Line endings and final newline of the text according to a complete format.</summary>
    internal static string Apply(string text, NewFileFormat format)
    {
        var newline = format.Crlf!.Value ? "\r\n" : "\n";
        var normalized = WithLineEndings(text, newline);

        // "No final newline" means "do not add one", not "remove it" (editorconfig).
        if (format.FinalNewline == true && normalized.Length > 0 && !normalized.EndsWith("\n", StringComparison.Ordinal))
            normalized += newline;

        return normalized;
    }

    /// <summary>
    /// Line endings and final newline of the text as the existing file had them. Unknown line endings (mixed, none)
    /// are kept; the final newline is restored both ways — added if it was there, removed if it was not.
    /// </summary>
    internal static string ApplyExisting(string text, WriteState state)
    {
        string newline;
        var result = text;
        if (state.Crlf != null)
        {
            newline = state.Crlf.Value ? "\r\n" : "\n";
            result = WithLineEndings(text, newline);
        }
        else
        {
            newline = LastLineBreak(text) ?? "\r\n";
        }

        var breakLength = TrailingBreakLength(result);
        if (state.FinalNewline && breakLength == 0 && result.Length > 0)
            result += newline;
        else if (!state.FinalNewline && breakLength > 0)
            result = result.Substring(0, result.Length - breakLength);

        return result;
    }

    internal static byte[] Encode(string text, bool bom)
    {
        var body = Utf8NoBom.GetBytes(text);
        if (!bom)
            return body;

        var bytes = new byte[Utf8Bom.Length + body.Length];
        Utf8Bom.CopyTo(bytes, 0);
        body.CopyTo(bytes, Utf8Bom.Length);
        return bytes;
    }

    // Writes the new bytes only if they differ, keeping the last write time; true if the file was rewritten.
    private static bool Rewrite(string path, byte[] original, byte[] bytes)
    {
        if (bytes.AsSpan().SequenceEqual(original))
            return false;

        var lastWrite = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, lastWrite);
        return true;
    }

    // The text without its BOM; null if the bytes are not valid UTF-8.
    private static string? Decode(byte[] bytes)
    {
        var start = HasBom(bytes) ? Utf8Bom.Length : 0;
        try
        {
            return StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsUtf8(byte[] bytes, int start)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes, start, bytes.Length - start);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool HasBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];

    private static string WithLineEndings(string text, string newline)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return newline == "\n" ? normalized : normalized.Replace("\n", newline);
    }

    // The kind of the text's last line break: what a restored final newline uses when line endings are kept.
    private static string? LastLineBreak(string text)
    {
        var i = text.LastIndexOfAny(new[] { '\r', '\n' });
        if (i < 0) return null;
        if (text[i] == '\n') return i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n";
        return "\r";
    }

    private static int TrailingBreakLength(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal)) return 2;
        return text.Length > 0 && (text[text.Length - 1] == '\n' || text[text.Length - 1] == '\r') ? 1 : 0;
    }
}