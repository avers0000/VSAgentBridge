using System.Globalization;
using System.Text;

namespace VSAgentBridge.Logic;

/// <summary>
/// Disk work in one session folder under Output: creates the folder and its _vsab-session.txt, copies originals
/// and clears read-only on copies (redirection, slice 3 results, §1.3, §1.5), and records refused deletes and
/// moves in _pending-operations.txt (slice 4 results, §1.6). No COM. Paths come from <see cref="OutputLayout"/>.
/// </summary>
internal sealed class OutputStore
{
    // A request of a few lines is enough to see what the agent wanted; a script pasted into a command is cut.
    private const int MaxRecordedRequestLength = 2000;

    // Two hook processes of one session may append at once; the second waits for the first.
    private const int RecordAttempts = 20;
    private const int RecordRetryDelayMs = 50;

    private readonly string _sessionFolder;
    private readonly string? _session;
    private readonly string? _harness;
    private readonly string? _cwd;

    public OutputStore(string sessionFolder, string? session, string? harness, string? cwd)
    {
        _sessionFolder = sessionFolder;
        _session = session;
        _harness = harness;
        _cwd = cwd;
    }

    /// <summary>
    /// An existing file is redirected (before): the tool works on a copy of it.
    /// If the session already has the copy, it is reused untouched (all edits of a session collect in one copy);
    /// otherwise the original is copied byte for byte and the copy is made writable.
    /// </summary>
    public OutputResult PrepareCopy(string originalPath, string targetPath) =>
        Run(targetPath, () =>
        {
            if (File.Exists(targetPath))
                return new OutputResult(targetPath, copied: false, reused: true, error: null);

            CopyFresh(originalPath, targetPath, overwrite: false);
            return new OutputResult(targetPath, copied: true, reused: false, error: null);
        });

    /// <summary>
    /// A file the model is about to create is redirected (before): only the folders are prepared.
    /// A file already there is an earlier redirect of this session; the tool then edits or replaces it.
    /// </summary>
    public OutputResult PrepareNewFile(string targetPath) =>
        Run(targetPath, () => new OutputResult(targetPath, copied: false, reused: File.Exists(targetPath), error: null));

    /// <summary>
    /// A new file was created in place but not added to a project (after): it stays where it is, and a copy goes
    /// to the session folder, so that the rest of the session works on the copy (reads and edits are redirected to it).
    /// The file was just written by the tool, so it replaces a copy that may already exist.
    /// </summary>
    public OutputResult CopyCreatedFile(string createdPath, string targetPath) =>
        Run(targetPath, () =>
        {
            var existed = File.Exists(targetPath);
            CopyFresh(createdPath, targetPath, overwrite: true);
            return new OutputResult(targetPath, copied: true, reused: existed, error: null);
        });

    /// <summary>Where <see cref="RecordPendingOperation"/> appends.</summary>
    public string PendingOperationsPath => Path.Combine(_sessionFolder, OutputLayout.PendingOperationsFileName);

    /// <summary>
    /// A delete or move was refused: appends what the agent asked for, so that the user sees it whatever the model
    /// says at the end of its answer. Creates the session folder (with its info file) if needed.
    /// </summary>
    /// <param name="tool">Harness tool name (shell, patch, ...).</param>
    /// <param name="request">The command or the patch operation as the agent gave it.</param>
    /// <param name="details">One line each: what was found, with paths where known.</param>
    /// <returns>null when recorded; otherwise why not (the refusal stands anyway).</returns>
    public string? RecordPendingOperation(string tool, string request, IEnumerable<string> details)
    {
        var entry = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("  ").Append(_harness ?? "(unknown)")
            .Append("  ").AppendLine(tool);

        foreach (var line in Indent(Shorten(request.Trim())))
            entry.Append("  > ").AppendLine(line);
        foreach (var detail in details)
            entry.Append("  ").AppendLine(detail);
        entry.AppendLine();

        try
        {
            EnsureSessionFolder();
            AppendWithRetry(PendingOperationsPath, entry.ToString());
            return null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                   || ex is ArgumentException || ex is NotSupportedException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    // Creates the session folder (with its info file) and the target's folder, then does the work.
    // Disk errors become a result: the hook turns them into a denial instead of crashing.
    private OutputResult Run(string targetPath, Func<OutputResult> work)
    {
        try
        {
            EnsureSessionFolder();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            return work();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                   || ex is ArgumentException || ex is NotSupportedException)
        {
            // PathTooLongException and DirectoryNotFoundException are IOExceptions.
            return OutputResult.Failed(targetPath, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void EnsureSessionFolder()
    {
        Directory.CreateDirectory(_sessionFolder);

        var infoPath = Path.Combine(_sessionFolder, OutputLayout.SessionInfoFileName);
        if (File.Exists(infoPath))
            return;

        var text = new StringBuilder()
            .AppendLine("VS Agent Bridge session folder: what this agent session could not do in place.")
            .AppendLine()
            .AppendLine(OutputLayout.PendingOperationsFileName + " (if present): deletes, moves and renames the bridge refused;")
            .AppendLine("do them in Visual Studio.")
            .AppendLine()
            .AppendLine("Every other file is the latest version from this session of a file it could not write in place.")
            .AppendLine("Apply it to the same relative path under cwd (files under _outside: to the full path shown by")
            .AppendLine("their folders) by comparing it with the original there, not by copying over it: the original")
            .AppendLine("may have changed while the session worked on the copy.")
            .AppendLine()
            .AppendLine("session: " + (_session ?? "(none)"))
            .AppendLine("harness: " + (_harness ?? "(unknown)"))
            .AppendLine("cwd: " + (_cwd ?? "(unknown)"))
            .AppendLine("created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .ToString();

        try
        {
            // CreateNew: two hook processes of one session may get here at once; the second one just skips.
            // StreamWriter writes the BOM itself; Encoding.GetBytes never does, whatever the encoding's settings.
            using var stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.Write(text);
        }
        catch (IOException) when (File.Exists(infoPath))
        {
            // Written by a parallel call meanwhile: fine.
        }
    }

    // Append under an exclusive lock (as the log does); a sharing violation means another hook process is appending right now.
    // A new file gets a UTF-8 BOM (StreamWriter writes it at position 0 only), so that Notepad shows paths in any language.
    private static void AppendWithRetry(string path, string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(text);
                return;
            }
            catch (IOException) when (attempt < RecordAttempts && File.Exists(path))
            {
                Thread.Sleep(RecordRetryDelayMs);
            }
        }
    }

    private static string Shorten(string text) =>
        text.Length <= MaxRecordedRequestLength ? text : text.Substring(0, MaxRecordedRequestLength) + " [...]";

    // A multi-line command keeps its lines, each marked as part of the request.
    private static IEnumerable<string> Indent(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd('\r'));

    private static void CopyFresh(string sourcePath, string targetPath, bool overwrite)
    {
        if (overwrite && File.Exists(targetPath))
            ClearReadOnly(targetPath); // File.Copy cannot overwrite a read-only file

        File.Copy(sourcePath, targetPath, overwrite);

        // File.Copy keeps the source attributes: a TFVC file would stay read-only and the tool would fail on it.
        ClearReadOnly(targetPath);
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}