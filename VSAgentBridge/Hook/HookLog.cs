using System.Diagnostics;
using System.Globalization;
using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Hook;

/// <summary>
/// Collects key=value fields of one hook call and appends them as one line to
/// Logs\vsagentbridge-yyyyMMdd.log next to the exe, if the configured verbosity reaches the line's importance.
/// Several hook processes may write at once: each line is a single write under an exclusive lock,
/// retried on sharing violations. Old daily files are deleted when a new day's file is started.
/// Logging never fails the hook.
/// </summary>
internal sealed class HookLog
{
    private const string FilePrefix = "vsagentbridge-";
    private const string FileExtension = ".log";
    private const string DateFormat = "yyyyMMdd";
    private const int MaxAttempts = 40;
    private const int RetryDelayMs = 25;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly char[] SpecialChars = { ' ', '"', '\t', '\r', '\n', '=' };

    private readonly StringBuilder _fields = new();

    /// <summary>Null values are skipped.</summary>
    public void Add(string key, object? value)
    {
        if (value == null)
            return;
        _fields.Append(' ').Append(key).Append('=').Append(Format(value.ToString() ?? ""));
    }

    /// <param name="settings">Configured verbosity and retention.</param>
    /// <param name="importance">The least verbose level that still writes this line.</param>
    public void Write(BridgeSettings settings, LogVerbosity importance)
    {
        if (settings.LogLevel < importance)
            return;

        var now = DateTime.Now;
        int pid;
        using (var self = Process.GetCurrentProcess())
            pid = self.Id;

        var bytes = Utf8NoBom.GetBytes($"{now:yyyy-MM-dd HH:mm:ss.fff} pid={pid}{_fields}{Environment.NewLine}");

        try
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, FilePrefix + now.ToString(DateFormat, CultureInfo.InvariantCulture) + FileExtension);
            var startsNewFile = !File.Exists(file);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // FileShare.Read: other writers wait (sharing violation → retry), readers (Notepad, tail) are fine.
                    using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
                    stream.Write(bytes, 0, bytes.Length);
                    break;
                }
                catch (IOException) when (attempt < MaxAttempts)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }

            // Cleanup runs once a day: by the call that starts the day's file.
            if (startsNewFile && settings.LogRetentionDays > 0)
                DeleteOldFiles(folder, now, settings.LogRetentionDays);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"vsagentbridge: cannot write the log: {ex.Message}");
        }
    }

    // The date is taken from the file name, not from its timestamps: unambiguous, and files
    // that do not follow the naming pattern are never touched.
    private static void DeleteOldFiles(string folder, DateTime now, int retentionDays)
    {
        var cutoff = now.Date.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(folder, FilePrefix + "*" + FileExtension))
        {
            var stamp = Path.GetFileNameWithoutExtension(file).Substring(FilePrefix.Length);
            if (!DateTime.TryParseExact(stamp, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= cutoff)
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Another hook process may be deleting it at the same moment, or it is open; next day's cleanup retries.
            }
        }
    }

    // One call = one line: values with spaces or quotes are quoted, line breaks (exception stack traces) become " | ".
    private static string Format(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(SpecialChars) < 0)
            return value;

        var flat = value.Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ').Replace("\"", "\\\"");
        return "\"" + flat + "\"";
    }
}