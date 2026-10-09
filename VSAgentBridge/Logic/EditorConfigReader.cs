using System.Text;
using System.Text.RegularExpressions;

namespace VSAgentBridge.Logic;

/// <summary>
/// Level 1 of the new-file format (slice 5 results, §1.2): what .editorconfig files say for a path.
/// Only end_of_line, charset and insert_final_newline are read. Files are looked up from the file's folder upwards
/// until one with "root = true" or the drive root; nearer files override farther ones, later sections override
/// earlier ones (editorconfig.org). A file that cannot be read is skipped: the format must never fail the bridge.
/// </summary>
internal static class EditorConfigReader
{
    public const string FileName = ".editorconfig";

    private const string Unset = "unset";

    private static readonly Regex NumericRange = new(@"^-?\d+\.\.-?\d+$", RegexOptions.CultureInvariant);

    public static NewFileFormat Read(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var files = FindFiles(Path.GetDirectoryName(fullPath)!);

        bool? crlf = null, bom = null, finalNewline = null;

        // Farthest first, so that each nearer file overrides what the farther ones said.
        for (var i = files.Count - 1; i >= 0; i--)
        {
            var (directory, lines) = files[i];
            var relativePath = RelativePath(directory, fullPath);
            var inMatchingSection = false;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                if (line[0] == '[')
                {
                    var end = line.LastIndexOf(']');
                    inMatchingSection = end > 1 && Matches(line.Substring(1, end - 1), relativePath);
                    continue;
                }

                if (!inMatchingSection || !TrySplit(line, out var key, out var value))
                    continue;

                switch (key)
                {
                    case "end_of_line":
                        crlf = Apply(value, crlf, "crlf", "lf"); // "cr" is not a Windows format: ignored
                        break;
                    case "charset":
                        // The bridge writes UTF-8 only; latin1 or utf-16 do not change the BOM decision.
                        bom = Apply(value, bom, "utf-8-bom", "utf-8");
                        break;
                    case "insert_final_newline":
                        finalNewline = Apply(value, finalNewline, "true", "false");
                        break;
                }
            }
        }

        return new NewFileFormat(crlf, bom, finalNewline, NewFileFormat.SourceEditorConfig);
    }

    /// <summary>
    /// Whether a section glob matches a path relative to the folder of its .editorconfig ('/' separators).
    /// A glob without '/' matches the file name in any subfolder; with '/', it is anchored at that folder.
    /// </summary>
    internal static bool Matches(string glob, string relativePath)
    {
        var anchored = glob.IndexOf('/') >= 0;
        var body = anchored && glob[0] == '/' ? glob.Substring(1) : glob;
        var pattern = "^" + (anchored ? "" : "(?:.*/)?") + ToRegex(body) + "$";

        try
        {
            return Regex.IsMatch(relativePath, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return false; // a character class the regex engine rejects: the section is ignored
        }
    }

    // Nearest first; stops after a file that declares root = true.
    private static List<(string Directory, string[] Lines)> FindFiles(string startDirectory)
    {
        var result = new List<(string, string[])>();
        for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, FileName);
            string[] lines;
            try
            {
                if (!File.Exists(path))
                    continue;
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                continue;
            }

            result.Add((dir.FullName, lines));
            if (IsRoot(lines))
                break;
        }
        return result;
    }

    // "root = true" counts only in the preamble, before the first section.
    private static bool IsRoot(string[] lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
                return false;
            if (TrySplit(line, out var key, out var value) && key == "root")
                return value == "true";
        }
        return false;
    }

    private static string RelativePath(string directory, string fullPath)
    {
        var prefix = directory.TrimEnd('\\', '/') + "\\";
        return fullPath.Substring(prefix.Length).Replace('\\', '/');
    }

    private static bool TrySplit(string line, out string key, out string value)
    {
        var index = line.IndexOf('=');
        if (index <= 0)
        {
            key = value = "";
            return false;
        }
        key = line.Substring(0, index).Trim().ToLowerInvariant();
        value = line.Substring(index + 1).Trim().ToLowerInvariant();
        return true;
    }

    // "unset" removes the value; anything unknown leaves the previous one.
    private static bool? Apply(string value, bool? current, string whenTrue, string whenFalse) =>
        value == whenTrue ? true
        : value == whenFalse ? false
        : value == Unset ? null
        : current;

    // Glob → regex: * (not across '/'), ** (across), ?, [set], [!set], {a,b} (nested), {1..9}, \ escapes.
    private static string ToRegex(string glob)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '\\':
                    sb.Append(i + 1 < glob.Length ? Regex.Escape(glob[++i].ToString()) : @"\\");
                    break;

                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(?:.*/)?"); // "a/**/b" also matches "a/b"
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;

                case '?':
                    sb.Append("[^/]");
                    break;

                case '[':
                    {
                        var close = glob.IndexOf(']', i + 1);
                        if (close <= i + 1)
                        {
                            sb.Append(@"\[");
                            break;
                        }
                        var set = glob.Substring(i + 1, close - i - 1);
                        var negate = set[0] == '!';
                        if (negate)
                            set = set.Substring(1);
                        else if (set[0] == '^')
                            set = @"\" + set;
                        sb.Append('[').Append(negate ? "^" : "").Append(set.Replace("[", @"\[")).Append(']');
                        i = close;
                        break;
                    }

                case '{':
                    {
                        var close = FindClosingBrace(glob, i);
                        if (close < 0)
                        {
                            sb.Append(@"\{");
                            break;
                        }
                        var body = glob.Substring(i + 1, close - i - 1);
                        var alternatives = SplitTopLevel(body);
                        if (alternatives.Count > 1)
                            sb.Append("(?:").Append(string.Join("|", alternatives.Select(ToRegex))).Append(')');
                        else if (NumericRange.IsMatch(body))
                            sb.Append(@"-?\d+"); // the bounds are not checked: ranges are rare in practice
                        else
                            sb.Append(Regex.Escape("{" + body + "}"));
                        i = close;
                        break;
                    }

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.ToString();
    }

    private static int FindClosingBrace(string glob, int open)
    {
        var depth = 0;
        for (var i = open; i < glob.Length; i++)
        {
            if (glob[i] == '\\') { i++; continue; }
            if (glob[i] == '{') depth++;
            else if (glob[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\') { i++; continue; }
            if (body[i] == '{') depth++;
            else if (body[i] == '}') depth--;
            else if (body[i] == ',' && depth == 0)
            {
                parts.Add(body.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(body.Substring(start));
        return parts;
    }
}