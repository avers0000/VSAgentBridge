namespace VSAgentBridge.Logic;

/// <summary>
/// What "before" knew about the file, handed back with "after" (slice 6 results, §1.13). The adapter keeps the
/// "state" string of the "before" response and returns it unchanged in the "after" request; only the bridge
/// writes and reads it.
/// "new": the file did not exist; "after" runs the new-file flow.
/// "existing bom crlf final" (or nobom / lf / mixed / nofinal): the file existed; "after" only brings it back
/// to the format it had before the write. "mixed" covers mixed line endings and a file without line breaks:
/// line endings are then left as written.
/// "existing notutf8": the file was not valid UTF-8; "after" leaves it as written.
/// </summary>
internal sealed class WriteState
{
    private const string NewToken = "new";
    private const string ExistingToken = "existing";
    private const string NotUtf8Token = "notutf8";
    private const string BomToken = "bom";
    private const string NoBomToken = "nobom";
    private const string CrlfToken = "crlf";
    private const string LfToken = "lf";
    private const string MixedToken = "mixed";
    private const string FinalToken = "final";
    private const string NoFinalToken = "nofinal";

    private WriteState(bool isNew, bool notUtf8, bool bom, bool? crlf, bool finalNewline)
    {
        IsNew = isNew;
        NotUtf8 = notUtf8;
        Bom = bom;
        Crlf = crlf;
        FinalNewline = finalNewline;
    }

    /// <summary>The file did not exist before the write.</summary>
    public static WriteState New { get; } = new(isNew: true, notUtf8: false, bom: false, crlf: null, finalNewline: false);

    /// <summary>The file existed and was not valid UTF-8 (most likely a legacy code page).</summary>
    public static WriteState ExistingNotUtf8 { get; } = new(isNew: false, notUtf8: true, bom: false, crlf: null, finalNewline: false);

    /// <summary>The file existed, valid UTF-8, in this format.</summary>
    /// <param name="crlf">True: all line breaks CRLF; false: all LF; null: mixed, or no line breaks.</param>
    public static WriteState Existing(bool bom, bool? crlf, bool finalNewline) =>
        new(isNew: false, notUtf8: false, bom, crlf, finalNewline);

    public bool IsNew { get; }

    /// <summary>Existing file that was not valid UTF-8: its format is not restored.</summary>
    public bool NotUtf8 { get; }

    public bool Bom { get; }

    /// <summary>True: CRLF; false: LF; null: line endings are left as written.</summary>
    public bool? Crlf { get; }

    /// <summary>The file ended with a line break.</summary>
    public bool FinalNewline { get; }

    /// <summary>The "state" string; also what the log shows.</summary>
    public override string ToString()
    {
        if (IsNew) return NewToken;
        if (NotUtf8) return $"{ExistingToken} {NotUtf8Token}";

        var lineEndings = Crlf == null ? MixedToken : Crlf.Value ? CrlfToken : LfToken;
        return $"{ExistingToken} {(Bom ? BomToken : NoBomToken)} {lineEndings} {(FinalNewline ? FinalToken : NoFinalToken)}";
    }

    /// <summary>The state from its string; null if missing or not one this bridge writes.</summary>
    public static WriteState? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var tokens = value!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1 && tokens[0] == NewToken)
            return New;
        if (tokens.Length == 0 || tokens[0] != ExistingToken)
            return null;
        if (tokens.Length == 2 && tokens[1] == NotUtf8Token)
            return ExistingNotUtf8;
        if (tokens.Length != 4)
            return null;

        bool bom;
        if (tokens[1] == BomToken) bom = true;
        else if (tokens[1] == NoBomToken) bom = false;
        else return null;

        bool? crlf;
        if (tokens[2] == CrlfToken) crlf = true;
        else if (tokens[2] == LfToken) crlf = false;
        else if (tokens[2] == MixedToken) crlf = null;
        else return null;

        bool finalNewline;
        if (tokens[3] == FinalToken) finalNewline = true;
        else if (tokens[3] == NoFinalToken) finalNewline = false;
        else return null;

        return Existing(bom, crlf, finalNewline);
    }
}