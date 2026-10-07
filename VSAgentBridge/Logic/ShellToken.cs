namespace VSAgentBridge.Logic;

/// <summary>
/// One token of a shell command as <see cref="ShellTokenizer"/> splits it: a word (quotes and escapes removed)
/// or a redirection operator. The tokenizer knows no particular shell: it accepts what PowerShell, cmd and bash
/// have in common closely enough to find command names, their arguments and file redirections.
/// </summary>
internal sealed class ShellToken
{
    public ShellToken(string text, bool isRedirect, bool hasQuotes, bool hasExpansion, int start, int end)
    {
        Text = text;
        IsRedirect = isRedirect;
        HasQuotes = hasQuotes;
        HasExpansion = hasExpansion;
        Start = start;
        End = end;
    }

    /// <summary>
    /// A word: its value with quotes and escapes removed ("D:\a b\x.cs" → D:\a b\x.cs).
    /// A redirection: the operator as written (">", ">>", "2>", "*>", "&>", "2>&1", "<").
    /// </summary>
    public string Text { get; }

    /// <summary>A redirection operator; for a file redirection the target is the next token of the segment.</summary>
    public bool IsRedirect { get; }

    /// <summary>A word with a quoted part: an argument for a nested shell ("pwsh -c '...'") is one such word.</summary>
    public bool HasQuotes { get; }

    /// <summary>
    /// The word contains a variable or a substitution ($x, $env:TEMP, ${x}, $(...), %X%) outside single quotes:
    /// its real value is known only when the shell runs the command.
    /// </summary>
    public bool HasExpansion { get; }

    /// <summary>Position of the token's first character in the command text.</summary>
    public int Start { get; }

    /// <summary>Position after the token's last character in the command text.</summary>
    public int End { get; }

    public override string ToString() => Text;
}