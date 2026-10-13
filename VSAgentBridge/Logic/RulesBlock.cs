namespace VSAgentBridge.Logic;

/// <summary>
/// The bridge's rules block in the agent's global rules file (~\.claude\CLAUDE.md, ~\.config\opencode\AGENTS.md):
/// the text of AGENTS.global.sample.md between its markers, with the workspace roots filled in.
/// Pure string logic: reading and writing the files is up to the caller.
/// </summary>
internal static class RulesBlock
{
    public const string SampleFileName = "AGENTS.global.sample.md";
    public const string BeginMarker = "<!-- vsab:begin -->";
    public const string EndMarker = "<!-- vsab:end -->";

    /// <summary>The sample's line that is replaced with one line per workspace root (its indentation is kept).</summary>
    public const string RootsPlaceholder = "- <folder with VS Agent Bridge>";

    /// <summary>
    /// The block for <paramref name="roots"/>: the sample's lines from the begin marker to the end marker,
    /// the placeholder line replaced with "- &lt;root&gt;" lines. Line breaks are "\n"; <see cref="Merge"/>
    /// converts them to those of the target file. Returns false with a message if the sample is broken.
    /// </summary>
    public static bool TryRender(string sample, IReadOnlyList<string> roots, out string? block, out string? error)
    {
        block = null;
        if (roots.Count == 0)
        {
            error = "no workspace roots to put into the rules";
            return false;
        }

        var lines = LineSpans(sample).Select(s => sample.Substring(s.Start, s.Length)).ToList();
        var begins = IndexesOf(lines, BeginMarker);
        var ends = IndexesOf(lines, EndMarker);
        if (begins.Count != 1 || ends.Count != 1 || begins[0] > ends[0])
        {
            error = $"{SampleFileName}: the markers {BeginMarker} and {EndMarker} must appear once each, begin first";
            return false;
        }

        var placeholders = Enumerable.Range(begins[0], ends[0] - begins[0] + 1)
            .Where(i => lines[i].Trim() == RootsPlaceholder)
            .ToList();
        if (placeholders.Count != 1)
        {
            error = $"{SampleFileName}: the line \"{RootsPlaceholder}\" must appear once between the markers";
            return false;
        }

        var result = new List<string>();
        for (var i = begins[0]; i <= ends[0]; i++)
        {
            if (i == placeholders[0])
            {
                var line = lines[i];
                var indent = line.Substring(0, line.Length - line.TrimStart().Length);
                result.AddRange(roots.Select(root => $"{indent}- {root}"));
            }
            else
            {
                // The begin line of the sample may start with its BOM.
                result.Add(i == begins[0] ? BeginMarker : lines[i]);
            }
        }

        block = string.Join("\n", result);
        error = null;
        return true;
    }

    /// <summary>
    /// Puts <paramref name="block"/> into the rules file text <paramref name="text"/> (null: the file does not exist).
    /// <paramref name="result"/> is the new text, or null for <see cref="RulesBlockChange.Ambiguous"/>.
    /// Everything outside the block stays as it was.
    /// </summary>
    public static RulesBlockChange Merge(string? text, string block, out string? result)
    {
        text ??= "";
        var newLine = DetectNewLine(text);
        var blockText = block.Replace("\r\n", "\n").Replace("\n", newLine);

        var spans = LineSpans(text);
        var lines = spans.Select(s => text.Substring(s.Start, s.Length)).ToList();
        var begins = IndexesOf(lines, BeginMarker);
        var ends = IndexesOf(lines, EndMarker);

        if (begins.Count == 0 && ends.Count == 0)
        {
            if (text.Trim().Length == 0)
            {
                result = blockText + newLine;
            }
            else
            {
                // A blank line between the user's text and the block.
                var head = text.EndsWith("\n", StringComparison.Ordinal) ? text : text + newLine;
                result = head + newLine + blockText + newLine;
            }
            return RulesBlockChange.Appended;
        }

        if (begins.Count != 1 || ends.Count != 1 || begins[0] > ends[0])
        {
            result = null;
            return RulesBlockChange.Ambiguous;
        }

        var start = spans[begins[0]].Start;
        var end = spans[ends[0]].Start + spans[ends[0]].Length;
        result = text.Substring(0, start) + blockText + text.Substring(end);
        return result == text ? RulesBlockChange.Unchanged : RulesBlockChange.Replaced;
    }

    // Line i of the text: start offset and length without its line break ("\r\n" or "\n").
    // A text ending with a line break has an empty last line.
    private static List<(int Start, int Length)> LineSpans(string text)
    {
        var spans = new List<(int Start, int Length)>();
        var start = 0;
        while (true)
        {
            var lineFeed = text.IndexOf('\n', start);
            if (lineFeed < 0)
            {
                spans.Add((start, text.Length - start));
                return spans;
            }

            var length = lineFeed - start;
            if (length > 0 && text[lineFeed - 1] == '\r')
                length--;
            spans.Add((start, length));
            start = lineFeed + 1;
        }
    }

    // A marker is a line of its own; spaces around it and a BOM before it are allowed.
    private static List<int> IndexesOf(List<string> lines, string marker) =>
        Enumerable.Range(0, lines.Count)
            .Where(i => lines[i].Trim(' ', '\t', '\uFEFF') == marker)
            .ToList();

    private static string DetectNewLine(string text)
    {
        var index = text.IndexOf('\n');
        if (index < 0)
            return "\r\n";
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }
}