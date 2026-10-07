using System.Text;
using System.Text.RegularExpressions;

namespace VSAgentBridge.Logic;

/// <summary>
/// Splits a shell command into segments (simple commands) and tokens, close enough for PowerShell, cmd and bash
/// to find command names, their arguments and file redirections (slice 4 results, §1.3). Never throws:
/// any text gives some result (an unclosed quote runs to the end of the text, an unknown character is part of a word).
/// Segments end at ; | || && & ( ) { } and line breaks outside quotes, so commands in pipelines, script blocks
/// and $(...) start their own segments.
/// Not a parser of any one shell: where the shells disagree, the reading that keeps Windows paths intact wins
/// (a backslash is a path separator, not an escape).
/// </summary>
internal sealed class ShellTokenizer
{
    // %NAME% outside a cmd variable is rare enough to treat any such word as expanded at run time.
    private static readonly Regex CmdVariable = new(@"%[A-Za-z_][A-Za-z0-9_]*%", RegexOptions.CultureInvariant);

    private static readonly Regex DoubleQuotedExpansion = new(@"\$[A-Za-z0-9_{(?]", RegexOptions.CultureInvariant);

    private readonly string _s;
    private readonly List<IReadOnlyList<ShellToken>> _segments = new();
    private List<ShellToken> _segment = new();
    private readonly StringBuilder _word = new();
    private readonly List<(string Delimiter, bool StripTabs)> _heredocs = new();

    private int _i;
    private bool _inWord;
    private bool _quoted;
    private bool _expansion;
    private int _wordStart;

    // Braces that started a segment ("{ rm x }", "% { ... }"); only those end one. A brace inside a word
    // (bash "file.{cs,bak}") stays part of the word.
    private int _braceDepth;

    private ShellTokenizer(string command) => _s = command;

    /// <summary>The non-empty segments of <paramref name="command"/>, each a list of tokens in order.</summary>
    public static IReadOnlyList<IReadOnlyList<ShellToken>> Split(string command) =>
        new ShellTokenizer(command ?? "").Run();

    private IReadOnlyList<IReadOnlyList<ShellToken>> Run()
    {
        while (_i < _s.Length)
        {
            var c = _s[_i];

            // Line continuation: PowerShell `, bash \, cmd ^ right before a line break.
            if ((c == '`' || c == '\\' || c == '^') && IsLineBreakAt(_i + 1))
            {
                EndWord();
                _i = AfterLineBreak(_i + 1);
                continue;
            }

            switch (c)
            {
                case '\n':
                    EndWord();
                    EndSegment();
                    _i++;
                    SkipHeredocBodies();
                    break;

                case ' ':
                case '\t':
                case '\r':
                    EndWord();
                    _i++;
                    break;

                case '#' when !_inWord:
                    // Comment (PowerShell, bash) up to the line break.
                    while (_i < _s.Length && _s[_i] != '\n') _i++;
                    break;

                case '\'':
                    ReadSingleQuoted();
                    break;

                case '"':
                    ReadDoubleQuoted();
                    break;

                case '@' when !_inWord && IsHereStringStart():
                    ReadHereString();
                    break;

                case '$':
                    ReadDollar();
                    break;

                case '\\' when Peek(1) == ';':
                    // find ... -exec rm {} \;  — an escaped ';' is a word, not a separator.
                    Append(';');
                    _i += 2;
                    break;

                case '>':
                    ReadOutputRedirect();
                    break;

                case '&' when Peek(1) == '>' && !_inWord:
                    ReadOutputRedirect(); // bash &> file: both streams
                    break;

                case '<':
                    ReadInputRedirect();
                    break;

                case ';':
                case '|':
                case '&':
                case '(':
                case ')':
                    EndWord();
                    EndSegment();
                    _i++;
                    break;

                case '{':
                    OpenBrace();
                    break;

                case '}':
                    CloseBrace();
                    break;

                default:
                    Append(c);
                    _i++;
                    break;
            }
        }

        EndWord();
        EndSegment();
        return _segments;
    }

    // ---- Words ----

    private void BeginWord()
    {
        if (_inWord) return;
        _inWord = true;
        _wordStart = _i;
    }

    private void Append(char c)
    {
        BeginWord();
        _word.Append(c);
    }

    private void EndWord()
    {
        if (!_inWord) return;

        var text = _word.ToString();
        var expansion = _expansion || CmdVariable.IsMatch(text);
        _segment.Add(new ShellToken(text, isRedirect: false, _quoted, expansion, _wordStart, _i));
        ResetWord();
    }

    private void ResetWord()
    {
        _word.Clear();
        _inWord = false;
        _quoted = false;
        _expansion = false;
    }

    private void EndSegment()
    {
        if (_segment.Count == 0) return;
        _segments.Add(_segment);
        _segment = new List<ShellToken>();
    }

    // 'text': literal; '' inside is one quote (PowerShell).
    private void ReadSingleQuoted()
    {
        BeginWord();
        _quoted = true;
        _i++;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '\'')
            {
                if (Peek(1) == '\'')
                {
                    _word.Append('\'');
                    _i += 2;
                    continue;
                }
                _i++;
                return;
            }
            _word.Append(c);
            _i++;
        }
    }

    // "text": "" (PowerShell, cmd) and `" (PowerShell) are a quote; \" (bash) too, unless it closes the string:
    // "D:\dir\" followed by a space or the end is a path ending with a backslash (cmd), not an escape.
    private void ReadDoubleQuoted()
    {
        BeginWord();
        _quoted = true;
        _i++;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '"')
            {
                if (Peek(1) == '"')
                {
                    _word.Append('"');
                    _i += 2;
                    continue;
                }
                _i++;
                return;
            }

            if (c == '`' && _i + 1 < _s.Length)
            {
                _word.Append(_s[_i + 1]);
                _i += 2;
                continue;
            }

            if (c == '\\' && Peek(1) == '"' && !EndsWordAt(_i + 2))
            {
                _word.Append('"');
                _i += 2;
                continue;
            }

            if (c == '$' && IsExpansionStart(Peek(1)))
                _expansion = true;

            _word.Append(c);
            _i++;
        }
    }

    // PowerShell here-string: @" or @' at the end of a line, closed by "@ or '@ at the start of a line.
    // Its body is data (the content of a file being written), never commands.
    private bool IsHereStringStart()
    {
        var q = Peek(1);
        if (q != '"' && q != '\'') return false;

        var j = _i + 2;
        while (j < _s.Length && (_s[j] == ' ' || _s[j] == '\t' || _s[j] == '\r')) j++;
        return j < _s.Length && _s[j] == '\n';
    }

    private void ReadHereString()
    {
        var q = _s[_i + 1];
        BeginWord();
        _quoted = true;

        var lineBreak = _s.IndexOf('\n', _i);
        var bodyStart = lineBreak + 1;
        var close = _s.IndexOf("\n" + q + "@", lineBreak, StringComparison.Ordinal);

        string body;
        if (close < 0)
        {
            body = _s.Substring(bodyStart);
            _i = _s.Length;
        }
        else
        {
            body = close < bodyStart ? "" : _s.Substring(bodyStart, close - bodyStart).TrimEnd('\r');
            _i = close + 3;
        }

        if (q == '"' && DoubleQuotedExpansion.IsMatch(body))
            _expansion = true;

        _word.Append(body);
    }

    // $name, $env:X, $_, ${...} mark the word as expanded at run time; $( ends the word, so the command inside
    // the parentheses becomes its own segment.
    private void ReadDollar()
    {
        BeginWord();
        var next = Peek(1);

        if (next == '(')
        {
            _expansion = true;
            _word.Append('$');
            _i++;
            EndWord();
            return;
        }

        if (next == '{')
        {
            _expansion = true;
            var close = _s.IndexOf('}', _i);
            var end = close < 0 ? _s.Length : close + 1;
            _word.Append(_s, _i, end - _i);
            _i = end;
            return;
        }

        if (IsExpansionStart(next))
            _expansion = true;

        _word.Append('$');
        _i++;
    }

    private void OpenBrace()
    {
        // "{}" is a word (find -exec ... {} ;).
        if (Peek(1) == '}')
        {
            Append('{');
            Append('}');
            _i += 2;
            return;
        }

        // A script block: "{ rm x }", "%{ rm $_ }", "?{ ... }". Inside a word ("file.{cs,bak}") the brace is text.
        if (!_inWord || (!_quoted && (_word.ToString() == "%" || _word.ToString() == "?")))
        {
            EndWord();
            EndSegment();
            _braceDepth++;
            _i++;
            return;
        }

        Append('{');
        _i++;
    }

    private void CloseBrace()
    {
        if (_braceDepth > 0)
        {
            EndWord();
            EndSegment();
            _braceDepth--;
            _i++;
            return;
        }

        Append('}');
        _i++;
    }

    // ---- Redirections ----

    // > >> >| 2> 2>> *> &> and stream merges 2>&1, >&2, 2>&-. A digit or * typed right before > belongs to it.
    // The target of a file redirection is the next token; a merge has none.
    private void ReadOutputRedirect()
    {
        var op = new StringBuilder();
        int start;

        var prefix = _word.ToString();
        if (_inWord && !_quoted && (prefix == "*" || (prefix.Length == 1 && char.IsDigit(prefix[0]))))
        {
            op.Append(prefix);
            start = _wordStart;
            ResetWord();
        }
        else
        {
            EndWord();
            start = _i;
        }

        if (_s[_i] == '&')
        {
            op.Append('&');
            _i++;
        }

        op.Append('>');
        _i++;

        if (Peek(0) == '>' || Peek(0) == '|')
        {
            op.Append(_s[_i]);
            _i++;
        }

        if (Peek(0) == '&' && (char.IsDigit(Peek(1)) || Peek(1) == '-'))
        {
            op.Append('&').Append(Peek(1));
            _i += 2;
        }

        _segment.Add(new ShellToken(op.ToString(), isRedirect: true, hasQuotes: false, hasExpansion: false, start, _i));
    }

    // < file and <<< word: input, the next token is data. << DELIM: a bash here-document, its body (the lines after
    // this one, up to DELIM) is data and is skipped at the next line break.
    private void ReadInputRedirect()
    {
        EndWord();
        var start = _i;

        if (Peek(1) == '<' && Peek(2) == '<')
        {
            _i += 3;
            _segment.Add(new ShellToken("<<<", isRedirect: true, hasQuotes: false, hasExpansion: false, start, _i));
            return;
        }

        if (Peek(1) == '<')
        {
            _i += 2;
            var stripTabs = Peek(0) == '-';
            if (stripTabs) _i++;

            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t')) _i++;

            var delimiter = new StringBuilder();
            while (_i < _s.Length && !char.IsWhiteSpace(_s[_i]) && ";|&<>".IndexOf(_s[_i]) < 0)
            {
                if (_s[_i] != '\'' && _s[_i] != '"' && _s[_i] != '\\')
                    delimiter.Append(_s[_i]);
                _i++;
            }

            if (delimiter.Length > 0)
                _heredocs.Add((delimiter.ToString(), stripTabs));
            return;
        }

        _i++;
        _segment.Add(new ShellToken("<", isRedirect: true, hasQuotes: false, hasExpansion: false, start, _i));
    }

    private void SkipHeredocBodies()
    {
        foreach (var (delimiter, stripTabs) in _heredocs)
        {
            while (_i < _s.Length)
            {
                var lineEnd = _s.IndexOf('\n', _i);
                var line = (lineEnd < 0 ? _s.Substring(_i) : _s.Substring(_i, lineEnd - _i)).TrimEnd('\r');
                if (stripTabs) line = line.TrimStart('\t');
                _i = lineEnd < 0 ? _s.Length : lineEnd + 1;
                if (line == delimiter) break;
            }
        }
        _heredocs.Clear();
    }

    // ---- Helpers ----

    private char Peek(int offset) => _i + offset < _s.Length ? _s[_i + offset] : '\0';

    private bool IsLineBreakAt(int k) =>
        k < _s.Length && (_s[k] == '\n' || (_s[k] == '\r' && k + 1 < _s.Length && _s[k + 1] == '\n'));

    private int AfterLineBreak(int k) => _s[k] == '\r' ? k + 2 : k + 1;

    private bool EndsWordAt(int k) =>
        k >= _s.Length || char.IsWhiteSpace(_s[k]) || ";|&()<>".IndexOf(_s[k]) >= 0;

    private static bool IsExpansionStart(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '{' || c == '(' || c == '?';
}