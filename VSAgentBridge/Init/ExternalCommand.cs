using System.Diagnostics;
using System.Text;

namespace VSAgentBridge.Init;

/// <summary>
/// An agent CLI (claude, opencode) found in PATH, and running it for init. A .exe or .com runs directly,
/// a .cmd or .bat through cmd.exe. Standard input is closed at once, so a command that asks something
/// gets end of input instead of waiting; a command that does not finish in time is killed.
/// </summary>
internal sealed class ExternalCommand
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] DirectExtensions = { ".exe", ".com" };
    private static readonly string[] CmdExtensions = { ".cmd", ".bat" };

    private ExternalCommand(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>The name it was found by ("claude").</summary>
    public string Name { get; }

    /// <summary>The full path of the program or script found.</summary>
    public string Path { get; }

    private bool RunsThroughCmd =>
        CmdExtensions.Contains(System.IO.Path.GetExtension(Path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds <paramref name="name"/> in PATH the way the console does; null if not found.</summary>
    public static ExternalCommand? Find(string name)
    {
        var path = FindInPath(
            name,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"),
            File.Exists);
        return path == null ? null : new ExternalCommand(name, path);
    }

    /// <summary>
    /// Runs the command with <paramref name="arguments"/> and waits up to <paramref name="timeout"/>
    /// (default <see cref="DefaultTimeout"/>). Returns the exit code and both outputs (UTF-8);
    /// <c>TimedOut</c> is true if the process had to be killed (the exit code is then -1).
    /// </summary>
    public (int ExitCode, string Output, string Error, bool TimedOut) Run(IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var args = arguments.ToList();
        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (RunsThroughCmd)
        {
            // /s with the whole command line in one pair of quotes: cmd removes only those outer quotes.
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var line = string.Join(" ", new[] { Path }.Concat(args).Select(a => "\"" + EscapeForQuotes(a) + "\""));
            start.Arguments = "/d /s /c \"" + line + "\"";
        }
        else
        {
            start.FileName = Path;
            start.Arguments = string.Join(" ", args.Select(QuoteArgument));
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)(timeout ?? DefaultTimeout).TotalMilliseconds))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // It has just exited.
            }
            process.WaitForExit();
            return (-1, output.Result, error.Result, true);
        }

        process.WaitForExit(); // lets the asynchronous reads finish
        return (process.ExitCode, output.Result, error.Result, false);
    }

    /// <summary>
    /// The first "&lt;folder&gt;\&lt;name&gt;&lt;ext&gt;" that exists, going through the folders of
    /// <paramref name="pathVariable"/> and, in each, the extensions of <paramref name="pathExtVariable"/> in order.
    /// Only extensions this class can run count (.exe, .com, .cmd, .bat). Null if none.
    /// </summary>
    internal static string? FindInPath(string name, string? pathVariable, string? pathExtVariable, Func<string, bool> fileExists)
    {
        var extensions = (string.IsNullOrWhiteSpace(pathExtVariable) ? ".COM;.EXE;.BAT;.CMD" : pathExtVariable!)
            .Split(';')
            .Select(e => e.Trim())
            .Where(e => DirectExtensions.Concat(CmdExtensions).Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in (pathVariable ?? "").Split(';'))
        {
            var folder = entry.Trim().Trim('"');
            if (folder.Length == 0)
                continue;

            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = System.IO.Path.Combine(folder, name + extension);
                }
                catch (ArgumentException)
                {
                    break; // not a valid folder: skip it
                }

                if (fileExists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// One argument for a command line, parsed back by the program as exactly this string
    /// (CommandLineToArgvW rules): quoted if empty or containing spaces, tabs or quotes.
    /// </summary>
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return argument;
        return "\"" + EscapeForQuotes(argument) + "\"";
    }

    // The inside of a quoted argument: a quote becomes \", and backslashes before a quote or before the
    // closing quote are doubled, so that they stay backslashes.
    private static string EscapeForQuotes(string argument)
    {
        var result = new StringBuilder();
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                result.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        return result.ToString();
    }
}