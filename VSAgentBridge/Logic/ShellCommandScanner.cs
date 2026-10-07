using System.Text.RegularExpressions;

namespace VSAgentBridge.Logic;

/// <summary>
/// Finds the obvious ways a shell command changes files (slice 4 results, §1.1, §1.3): command names
/// of PowerShell, cmd and bash at the start of each segment, file redirections, nested shells (pwsh -c "...",
/// cmd /c ..., bash -c '...', iex) and [IO.File]:: calls. Scripts, encoded commands and other languages are not
/// looked into: AGENTS.md and the harness's permission prompt cover those.
/// Never throws on any text; whether anything was found never depends on the paths, which are collected only
/// for the texts and the record.
/// </summary>
internal static class ShellCommandScanner
{
    // pwsh -c "cmd /c 'bash -c ...'" is already unusual; deeper nesting is not followed.
    private const int MaxNesting = 4;

    // Commands recognized by name: what they do and how many of their positional arguments are paths (0: all).
    // Set-Content X.cs "text": the second positional argument is the content, not a path.
    private static readonly Dictionary<string, (ShellOperation Operation, int PositionalPaths)> Commands =
        new(StringComparer.Ordinal)
        {
            ["rm"] = (ShellOperation.Delete, 0),
            ["rmdir"] = (ShellOperation.Delete, 0),
            ["rd"] = (ShellOperation.Delete, 0),
            ["unlink"] = (ShellOperation.Delete, 0),
            ["del"] = (ShellOperation.Delete, 0),
            ["erase"] = (ShellOperation.Delete, 0),
            ["shred"] = (ShellOperation.Delete, 0),
            ["remove-item"] = (ShellOperation.Delete, 0),
            ["ri"] = (ShellOperation.Delete, 0),

            ["mv"] = (ShellOperation.Move, 0),
            ["move"] = (ShellOperation.Move, 0),
            ["ren"] = (ShellOperation.Move, 0),
            ["rename"] = (ShellOperation.Move, 0),
            ["move-item"] = (ShellOperation.Move, 0),
            ["mi"] = (ShellOperation.Move, 0),
            ["rename-item"] = (ShellOperation.Move, 0),
            ["rni"] = (ShellOperation.Move, 0),

            ["set-content"] = (ShellOperation.Write, 1),
            ["add-content"] = (ShellOperation.Write, 1),
            ["ac"] = (ShellOperation.Write, 1),
            ["out-file"] = (ShellOperation.Write, 1),
            ["tee-object"] = (ShellOperation.Write, 1),
            ["new-item"] = (ShellOperation.Write, 1),
            ["ni"] = (ShellOperation.Write, 1),
            ["clear-content"] = (ShellOperation.Write, 0),
            ["clc"] = (ShellOperation.Write, 0),
            ["tee"] = (ShellOperation.Write, 0),
            ["touch"] = (ShellOperation.Write, 0),
            ["truncate"] = (ShellOperation.Write, 0),

            ["copy"] = (ShellOperation.Copy, 0),
            ["cp"] = (ShellOperation.Copy, 0),
            ["cpi"] = (ShellOperation.Copy, 0),
            ["copy-item"] = (ShellOperation.Copy, 0),
            ["xcopy"] = (ShellOperation.Copy, 0),

            ["attrib"] = (ShellOperation.ClearReadOnly, 0),
            ["chmod"] = (ShellOperation.ClearReadOnly, 0),
        };

    // Words that come before the real command: "sudo rm", "xargs rm", "start /b del x", "call del x".
    private static readonly HashSet<string> Prefixes = Names(
        "sudo", "nohup", "time", "command", "exec", "call", "start", "env", "nice", "xargs", "builtin");

    private static readonly HashSet<string> XargsValueOptions = Names("-n", "-i", "-l", "-p", "-d", "-s", "-a", "-e");

    private static readonly HashSet<string> Shells = Names("bash", "sh", "zsh", "dash", "ksh");
    private static readonly HashSet<string> PowerShells = Names("powershell", "pwsh", "powershell_ise");

    // PowerShell parameters whose value is a path, and those whose value is not (it must not be taken as one).
    private static readonly HashSet<string> PathOptions = Names(
        "path", "literalpath", "lp", "pspath", "destination", "newname", "filepath", "target",
        "t", "target-directory");

    // New-Item -Path D:\x -Name a.cs: here -Name is the file name (in Set-ItemProperty it is a property name).
    private static readonly HashSet<string> NewItemPathOptions = new(PathOptions.Concat(new[] { "name" }), StringComparer.Ordinal);

    private static readonly HashSet<string> ValueOptions = Names(
        "value", "filter", "include", "exclude", "itemtype", "type", "encoding", "credential", "stream",
        "delimiter", "inputobject", "width", "depth", "erroraction", "ea", "warningaction", "wa",
        "informationaction", "infa", "errorvariable", "ev", "outvariable", "ov", "pipelinevariable", "pv",
        "outbuffer", "ob");

    // powershell.exe parameters that take a value before the command.
    private static readonly HashSet<string> PowerShellValueOptions = Names(
        "executionpolicy", "ep", "ex", "windowstyle", "w", "outputformat", "of", "inputformat", "if",
        "version", "v", "psconsolefile", "configurationname", "workingdirectory", "wd", "settingsfile");

    private static readonly HashSet<string> DiscardTargets = Names("$null", "nul", "nul:", "/dev/null", @"\\.\nul");

    private static readonly Regex DotNetCall = new(
        @"\[\s*(?:System\.)?IO\.(?<type>File|Directory)\s*\]\s*::\s*(?<method>[A-Za-z]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // (Get-Item x).IsReadOnly = $false
    private static readonly Regex ClearReadOnly = new(
        @"\.IsReadOnly\s*=\s*\$false", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Assignment = new(@"^[A-Za-z_][A-Za-z0-9_]*=", RegexOptions.CultureInvariant);

    /// <summary>Everything in <paramref name="command"/> that changes files, in order of appearance.</summary>
    public static IReadOnlyList<ShellFinding> Scan(string command)
    {
        var findings = new List<ShellFinding>();
        if (string.IsNullOrWhiteSpace(command))
            return findings;

        ScanDotNet(command, findings);
        ScanText(command, depth: 0, findings);
        return findings;
    }

    // ---- Text and segments ----

    private static void ScanText(string text, int depth, List<ShellFinding> findings)
    {
        foreach (var segment in ShellTokenizer.Split(text))
            ScanSegment(segment, text, depth, findings);
    }

    private static void ScanSegment(IReadOnlyList<ShellToken> segment, string source, int depth, List<ShellFinding> findings)
    {
        var words = new List<ShellToken>(segment.Count);

        for (var i = 0; i < segment.Count; i++)
        {
            var token = segment[i];
            if (!token.IsRedirect)
            {
                words.Add(token);
                continue;
            }

            // Input (<, <<<): the next word is data, not an argument.
            var op = token.Text;
            var target = i + 1 < segment.Count && !segment[i + 1].IsRedirect ? segment[i + 1] : null;
            if (op.StartsWith("<", StringComparison.Ordinal))
            {
                if (target != null) i++;
                continue;
            }

            // 2>&1, >&2: one stream into another, no file.
            if (IsStreamMerge(op) || target == null)
                continue;

            i++;
            if (DiscardTargets.Contains(target.Text.ToLowerInvariant()))
                continue;

            findings.Add(new ShellFinding(ShellOperation.Write, op, PathsOf(target)));
        }

        ScanWords(words, 0, source, depth, findings);
    }

    private static void ScanWords(IReadOnlyList<ShellToken> words, int from, string source, int depth, List<ShellFinding> findings)
    {
        var i = SkipPrefixes(words, from);
        if (i >= words.Count)
            return;

        var commandToken = words[i];
        var name = CommandName(commandToken.Text);
        var args = i + 1;

        if (Commands.TryGetValue(name, out var known))
        {
            var (operation, positionalPaths) = known;
            if (IsDirectoryCreation(name, words, args) || IsHarmlessAttributeChange(name, words, args))
                return;
            var paths = Arguments(words, args, positionalPaths, name == "new-item" || name == "ni" ? NewItemPathOptions : PathOptions);
            if (name == "chmod" && ChmodModeIsPositional(words, args))
                paths = paths.Skip(1).ToList(); // chmod 644 x / u+w x: the mode comes first
            Add(findings, operation, commandToken.Text, paths);
            return;
        }

        switch (name)
        {
            case "git":
                ScanGit(words, args, findings);
                return;

            case "tf":
                ScanTf(words, args, findings);
                return;

            case "robocopy":
                ScanRobocopy(commandToken.Text, words, args, findings);
                return;

            case "sed":
                ScanSed(commandToken.Text, words, args, findings);
                return;

            case "find":
                ScanFind(words, args, source, depth, findings);
                return;

            case "set-itemproperty":
            case "sp":
                if (words.Skip(args).Any(w => string.Equals(w.Text, "IsReadOnly", StringComparison.OrdinalIgnoreCase)))
                    Add(findings, ShellOperation.ClearReadOnly, commandToken.Text, Arguments(words, args, positionalPaths: 1));
                return;

            case "wsl":
                ScanWords(words, SkipOptions(words, args, Names("-d", "--distribution", "-u", "--user", "--cd")), source, depth, findings);
                return;
        }

        var nested = NestedScript(name, words, args, source);
        if (nested != null && depth < MaxNesting)
            ScanText(nested, depth + 1, findings);
    }

    // ---- Particular commands ----

    private static void ScanGit(IReadOnlyList<ShellToken> words, int from, List<ShellFinding> findings)
    {
        var i = from;
        while (i < words.Count && IsOption(words[i]))
            i += words[i].Text == "-C" || words[i].Text == "-c" ? 2 : 1; // git -C <dir> / -c <key=value>

        if (i >= words.Count) return;

        var sub = words[i].Text.ToLowerInvariant();
        if (sub == "rm" || sub == "clean")
            Add(findings, ShellOperation.Delete, "git " + sub, Arguments(words, i + 1, positionalPaths: 0));
        else if (sub == "mv")
            Add(findings, ShellOperation.Move, "git " + sub, Arguments(words, i + 1, positionalPaths: 0));
    }

    private static void ScanTf(IReadOnlyList<ShellToken> words, int from, List<ShellFinding> findings)
    {
        if (from >= words.Count) return;

        var sub = words[from].Text.ToLowerInvariant();
        if (sub == "delete" || sub == "del" || sub == "destroy")
            Add(findings, ShellOperation.Delete, "tf " + sub, Arguments(words, from + 1, positionalPaths: 0));
        else if (sub == "rename" || sub == "ren" || sub == "move")
            Add(findings, ShellOperation.Move, "tf " + sub, Arguments(words, from + 1, positionalPaths: 0));
    }

    // robocopy src dst [files] copies; /MOV and /MOVE move; /PURGE and /MIR delete what the source lacks.
    private static void ScanRobocopy(string command, IReadOnlyList<ShellToken> words, int from, List<ShellFinding> findings)
    {
        var options = words.Skip(from).Select(w => w.Text.ToLowerInvariant()).ToList();
        var operation = options.Contains("/purge") || options.Contains("/mir") ? ShellOperation.Delete
            : options.Contains("/mov") || options.Contains("/move") ? ShellOperation.Move
            : ShellOperation.Copy;

        Add(findings, operation, command, Arguments(words, from, positionalPaths: 2));
    }

    // sed -i edits files in place; without -i it only prints. The script is the first positional argument
    // unless -e/-f gives it; the files follow.
    private static void ScanSed(string command, IReadOnlyList<ShellToken> words, int from, List<ShellFinding> findings)
    {
        var inPlace = false;
        var scriptGiven = false;
        var positional = new List<ShellToken>();

        for (var i = from; i < words.Count; i++)
        {
            var w = words[i];
            if (!IsOption(w))
            {
                positional.Add(w);
                continue;
            }

            var t = w.Text;
            if (t == "--in-place" || t.StartsWith("--in-place=", StringComparison.Ordinal) || Regex.IsMatch(t, "^-[A-Za-z]*i"))
                inPlace = true;
            if (t == "-e" || t == "-f" || t == "--expression" || t == "--file")
            {
                scriptGiven = true;
                i++;
            }
        }

        if (!inPlace) return;

        var paths = new List<string>();
        foreach (var w in positional.Skip(scriptGiven ? 0 : 1))
            AddPaths(paths, w.Text, w.HasExpansion);
        Add(findings, ShellOperation.Write, command + " -i", paths);
    }

    // find ... -delete deletes; find ... -exec CMD ... ; runs CMD for each file.
    private static void ScanFind(IReadOnlyList<ShellToken> words, int from, string source, int depth, List<ShellFinding> findings)
    {
        var start = words.Skip(from).TakeWhile(w => !IsOption(w)).Where(w => !w.HasExpansion).Select(w => w.Text).ToList();

        for (var i = from; i < words.Count; i++)
        {
            var t = words[i].Text;
            if (t == "-delete")
            {
                Add(findings, ShellOperation.Delete, "find -delete", start);
                continue;
            }

            if (t == "-exec" || t == "-execdir" || t == "-ok" || t == "-okdir")
            {
                var end = i + 1;
                while (end < words.Count && words[end].Text != ";" && words[end].Text != "+") end++;
                ScanWords(words.Skip(i + 1).Take(end - i - 1).ToList(), 0, source, depth, findings);
                i = end;
            }
        }
    }

    // New-Item -ItemType Directory creates a folder: nothing is written into a file.
    private static bool IsDirectoryCreation(string name, IReadOnlyList<ShellToken> words, int from)
    {
        if (name != "new-item" && name != "ni") return false;

        for (var i = from; i < words.Count; i++)
        {
            if (!IsOption(words[i])) continue;
            var (option, inline) = SplitOption(words[i].Text);
            if (option != "itemtype" && option != "type" && option != "it") continue;

            var value = inline ?? (i + 1 < words.Count ? words[i + 1].Text : "");
            var v = value.ToLowerInvariant();
            return v == "directory" || v == "dir" || v == "d";
        }
        return false;
    }

    // attrib/chmod matter only when they make a file writable (attrib -R, chmod +w / u+w / 666 ...).
    private static bool IsHarmlessAttributeChange(string name, IReadOnlyList<ShellToken> words, int from)
    {
        if (name == "attrib")
            return !words.Skip(from).Any(w => string.Equals(w.Text, "-r", StringComparison.OrdinalIgnoreCase));
        if (name == "chmod")
            return !words.Skip(from).Any(w => w.Text.IndexOf('w') >= 0 || Regex.IsMatch(w.Text, "^[0-7]{3,4}$"));
        return false;
    }

    // "+w" is skipped as a switch already; "644" or "u+w" is the first positional argument.
    private static bool ChmodModeIsPositional(IReadOnlyList<ShellToken> words, int from)
    {
        var mode = words.Skip(from).FirstOrDefault(w => !IsOption(w));
        return mode != null && !mode.Text.StartsWith("+", StringComparison.Ordinal);
    }

    // ---- Nested shells ----

    // The script a nested shell runs: pwsh -Command <rest>, cmd /c <rest>, bash -c '<script>', iex <rest>.
    // null when there is none or it cannot be read (-File, -EncodedCommand).
    private static string? NestedScript(string name, IReadOnlyList<ShellToken> words, int from, string source)
    {
        if (PowerShells.Contains(name))
            return PowerShellScript(words, from, source);

        if (name == "cmd")
        {
            for (var i = from; i < words.Count; i++)
            {
                var t = words[i].Text.ToLowerInvariant();
                if (t == "/c" || t == "/k") return Rest(words, i + 1, source);
                if (!t.StartsWith("/", StringComparison.Ordinal)) return null;
            }
            return null;
        }

        if (Shells.Contains(name))
        {
            for (var i = from; i < words.Count; i++)
            {
                var t = words[i].Text;
                if (words[i].HasQuotes || !t.StartsWith("-", StringComparison.Ordinal)) return null;
                if (!t.StartsWith("--", StringComparison.Ordinal) && t.IndexOf('c') > 0)
                    return i + 1 < words.Count ? words[i + 1].Text : null; // bash -c takes one argument
            }
            return null;
        }

        if (name == "iex" || name == "invoke-expression")
        {
            var i = from;
            if (i < words.Count && IsOption(words[i]) && SplitOption(words[i].Text).Option.StartsWith("c", StringComparison.Ordinal))
                i++;
            return Rest(words, i, source);
        }

        return null;
    }

    private static string? PowerShellScript(IReadOnlyList<ShellToken> words, int from, string source)
    {
        for (var i = from; i < words.Count; i++)
        {
            if (!IsOption(words[i]))
                return Rest(words, i, source); // powershell "Remove-Item x": the command without -Command

            var option = SplitOption(words[i].Text).Option;
            if (option == "c" || (option.Length >= 3 && "command".StartsWith(option, StringComparison.Ordinal)))
                return Rest(words, i + 1, source);
            if (option == "f" || option == "file" || option == "e" || option == "ec"
                || (option.Length >= 3 && "encodedcommand".StartsWith(option, StringComparison.Ordinal)))
                return null;
            if (PowerShellValueOptions.Contains(option))
                i++;
        }
        return null;
    }

    // One word: its value (a quoted script). Several: the original text from the first to the last,
    // so that quotes inside stay as written.
    private static string? Rest(IReadOnlyList<ShellToken> words, int from, string source)
    {
        if (from >= words.Count) return null;
        if (from == words.Count - 1) return words[from].Text;

        var start = words[from].Start;
        var end = words[words.Count - 1].End;
        return start >= 0 && end <= source.Length && end > start ? source.Substring(start, end - start) : null;
    }

    // ---- .NET calls ----

    // [IO.File]::Delete(...), [System.IO.File]::WriteAllText(...), [IO.Directory]::Move(...).
    // Looked for in the whole text once (nested scripts are part of it).
    private static void ScanDotNet(string command, List<ShellFinding> findings)
    {
        foreach (Match m in DotNetCall.Matches(command))
        {
            var type = m.Groups["type"].Value.ToLowerInvariant();
            var method = m.Groups["method"].Value.ToLowerInvariant();
            var call = $"[IO.{m.Groups["type"].Value}]::{m.Groups["method"].Value}";

            var operation =
                method == "delete" ? ShellOperation.Delete
                : method == "move" || method == "replace" ? ShellOperation.Move
                : type != "file" ? (ShellOperation?)null
                : method == "copy" ? ShellOperation.Copy
                : method == "setattributes" ? ShellOperation.ClearReadOnly
                : method.StartsWith("read", StringComparison.Ordinal) || method.StartsWith("get", StringComparison.Ordinal)
                  || method.StartsWith("exists", StringComparison.Ordinal) || method.StartsWith("openread", StringComparison.Ordinal)
                  || method.StartsWith("opentext", StringComparison.Ordinal) ? null
                : ShellOperation.Write;

            if (operation != null)
                findings.Add(new ShellFinding(operation.Value, call, Array.Empty<string>()));
        }

        if (ClearReadOnly.IsMatch(command))
            findings.Add(new ShellFinding(ShellOperation.ClearReadOnly, "IsReadOnly = $false", Array.Empty<string>()));
    }

    // ---- Arguments ----

    private static int SkipPrefixes(IReadOnlyList<ShellToken> words, int i)
    {
        while (i < words.Count)
        {
            var w = words[i];
            if (w.Text.Length == 0 || (!w.HasQuotes && Assignment.IsMatch(w.Text)) || (w.Text == "." && !w.HasQuotes))
            {
                i++; // "" (start's window title), VAR=value, . (dot-sourcing)
                continue;
            }

            var name = CommandName(w.Text);
            if (!Prefixes.Contains(name))
                return i;

            i = SkipOptions(words, i + 1, name == "xargs" ? XargsValueOptions : null);
        }
        return i;
    }

    private static int SkipOptions(IReadOnlyList<ShellToken> words, int i, HashSet<string>? valueOptions)
    {
        while (i < words.Count && (IsOption(words[i]) || IsCmdSwitch(words[i])))
            i += valueOptions != null && valueOptions.Contains(words[i].Text.ToLowerInvariant()) ? 2 : 1;
        return i;
    }

    /// <summary>
    /// Paths among the arguments: values of path parameters (-Path, -Destination, ...) and the first
    /// <paramref name="positionalPaths"/> positional arguments (0: all). Options, switches and values of
    /// other parameters are skipped; a word with a variable gives no path.
    /// </summary>
    private static List<string> Arguments(
        IReadOnlyList<ShellToken> words, int from, int positionalPaths, HashSet<string>? pathOptions = null)
    {
        pathOptions ??= PathOptions;
        var paths = new List<string>();
        var positional = 0;
        var optionsEnded = false;

        for (var i = from; i < words.Count; i++)
        {
            var w = words[i];

            if (!optionsEnded && !w.HasQuotes && w.Text == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (!optionsEnded && IsOption(w))
            {
                var (option, inline) = SplitOption(w.Text);
                var takesValue = pathOptions.Contains(option) || ValueOptions.Contains(option);
                var value = inline != null ? null : takesValue && i + 1 < words.Count ? words[++i] : null;

                if (pathOptions.Contains(option))
                {
                    if (inline != null) AddPaths(paths, inline, hasExpansion: w.HasExpansion);
                    else if (value != null) AddPaths(paths, value.Text, value.HasExpansion);
                }
                continue;
            }

            // cmd switches (/s); attrib +r, chmod +w.
            if (!optionsEnded && (IsCmdSwitch(w) || (!w.HasQuotes && w.Text.StartsWith("+", StringComparison.Ordinal))))
                continue;

            positional++;
            if (positionalPaths == 0 || positional <= positionalPaths)
                AddPaths(paths, w.Text, w.HasExpansion);
        }

        return paths;
    }

    // PowerShell takes several paths as a comma list: Remove-Item a.cs, b.cs (or a.cs,b.cs).
    private static void AddPaths(List<string> paths, string text, bool hasExpansion)
    {
        if (hasExpansion) return;
        foreach (var part in text.Split(','))
        {
            var p = part.Trim();
            if (p.Length > 0) paths.Add(p);
        }
    }

    private static bool IsOption(ShellToken w) =>
        !w.HasQuotes && w.Text.Length > 1 && w.Text[0] == '-' && !char.IsDigit(w.Text[1]);

    // cmd and tf switches: /s, /q, /recursive, /a:h. A path like /d/work has a second slash.
    private static bool IsCmdSwitch(ShellToken w) =>
        !w.HasQuotes && w.Text.Length >= 2 && w.Text[0] == '/' && w.Text.IndexOfAny(new[] { '/', '\\', '.' }, 1) < 0;

    // "-Path:x" → ("path", "x"); "--target-directory=d" → ("target-directory", "d"); "-Force" → ("force", null).
    private static (string Option, string? Inline) SplitOption(string text)
    {
        var body = text.TrimStart('-');
        var cut = body.IndexOfAny(new[] { ':', '=' });
        return cut < 0
            ? (body.ToLowerInvariant(), null)
            : (body.Substring(0, cut).ToLowerInvariant(), body.Substring(cut + 1));
    }

    private static bool IsStreamMerge(string op)
    {
        var amp = op.IndexOf(">&", StringComparison.Ordinal);
        return amp >= 0 && amp + 2 < op.Length;
    }

    /// <summary>"C:\Windows\System32\cmd.exe" → "cmd"; "Remove-Item" → "remove-item"; "/bin/rm" → "rm".</summary>
    internal static string CommandName(string text)
    {
        var t = text.Trim();
        var slash = t.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0 && slash < t.Length - 1) t = t.Substring(slash + 1);
        t = t.ToLowerInvariant();

        foreach (var ext in new[] { ".exe", ".cmd", ".bat", ".com" })
        {
            if (t.EndsWith(ext, StringComparison.Ordinal) && t.Length > ext.Length)
                return t.Substring(0, t.Length - ext.Length);
        }
        return t;
    }

    private static IReadOnlyList<string> PathsOf(ShellToken target) =>
        target.HasExpansion ? Array.Empty<string>() : new[] { target.Text };

    private static void Add(List<ShellFinding> findings, ShellOperation operation, string command, IReadOnlyList<string> paths) =>
        findings.Add(new ShellFinding(operation, command, paths));

    private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);
}