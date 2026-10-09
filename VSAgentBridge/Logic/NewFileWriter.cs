using System.Text;

namespace VSAgentBridge.Logic;

/// <summary>
/// Writes a new file in its format (slice 5 results, §1.2), for both paths of a new file:
/// <see cref="Create"/> — create_file writes the model's content; <see cref="Normalize"/> — the hook's "after"
/// rewrites what the write tool wrote, keeping the file's last write time (so that the harness does not see
/// the file as changed since it wrote it). Always UTF-8; line endings, BOM and final newline from the format.
/// </summary>
internal static class NewFileWriter
{
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
            var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;

            string text;
            try
            {
                text = StrictUtf8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));
            }
            catch (DecoderFallbackException)
            {
                return (null, 0, false, "not valid UTF-8; left as written");
            }

            var format = NewFileFormatResolver.Resolve(path, scopeDirectory, text, out var formatMs);
            var bytes = Encode(Apply(text, format), format.Bom!.Value);
            if (bytes.AsSpan().SequenceEqual(original))
                return (format, formatMs, false, null);

            var lastWrite = File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, lastWrite);
            return (format, formatMs, true, null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return (null, 0, false, ex.Message);
        }
    }

    /// <summary>Line endings and final newline of the text according to a complete format.</summary>
    internal static string Apply(string text, NewFileFormat format)
    {
        var newline = format.Crlf!.Value ? "\r\n" : "\n";
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (newline != "\n")
            normalized = normalized.Replace("\n", newline);

        // "No final newline" means "do not add one", not "remove it" (editorconfig).
        if (format.FinalNewline == true && normalized.Length > 0 && !normalized.EndsWith("\n", StringComparison.Ordinal))
            normalized += newline;

        return normalized;
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
}