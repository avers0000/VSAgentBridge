namespace VSAgentBridge.Logic;

/// <summary>
/// Format of a new file: line endings, UTF-8 BOM, final newline. A value may be unset (null) when a source does not
/// say it: the same type describes what one level gave (.editorconfig, neighbor files, the Visual Studio default)
/// and the result of combining them with <see cref="Or"/> (slice 5 results, §1.2).
/// Each value keeps the name of the level it came from, for the log.
/// </summary>
internal sealed class NewFileFormat
{
    public const string SourceEditorConfig = "editorconfig";
    public const string SourceNeighbors = "neighbors";
    public const string SourceDefault = "default";

    /// <summary>What Add > New Item gives with the standard Visual Studio templates: UTF-8 with BOM, CRLF.</summary>
    public static NewFileFormat VisualStudioDefault { get; } =
        new(crlf: true, bom: true, finalNewline: true, SourceDefault);

    /// <summary>Nothing known.</summary>
    public static NewFileFormat None { get; } = new(null, null, null, source: "");

    /// <summary>All values from one source.</summary>
    public NewFileFormat(bool? crlf, bool? bom, bool? finalNewline, string source, bool neighborNotUtf8 = false)
        : this(crlf, crlf == null ? null : source,
               bom, bom == null ? null : source,
               finalNewline, finalNewline == null ? null : source,
               neighborNotUtf8)
    {
    }

    private NewFileFormat(
        bool? crlf, string? crlfSource, bool? bom, string? bomSource,
        bool? finalNewline, string? finalNewlineSource, bool neighborNotUtf8)
    {
        Crlf = crlf;
        CrlfSource = crlfSource;
        Bom = bom;
        BomSource = bomSource;
        FinalNewline = finalNewline;
        FinalNewlineSource = finalNewlineSource;
        NeighborNotUtf8 = neighborNotUtf8;
    }

    /// <summary>True: CRLF; false: LF; null: not given by this source.</summary>
    public bool? Crlf { get; }
    public string? CrlfSource { get; }

    /// <summary>True: UTF-8 with BOM; false: without; null: not given.</summary>
    public bool? Bom { get; }
    public string? BomSource { get; }

    /// <summary>True: the file ends with a line break; null: not given.</summary>
    public bool? FinalNewline { get; }
    public string? FinalNewlineSource { get; }

    /// <summary>
    /// Neighbor files of this type are not valid UTF-8 (most likely windows-1251). Their "no BOM" is then kept
    /// only for ASCII content: the bridge always writes UTF-8, and UTF-8 without BOM next to such files would be
    /// read as the legacy code page.
    /// </summary>
    public bool NeighborNotUtf8 { get; }

    /// <summary>All three values are set.</summary>
    public bool IsComplete => Crlf != null && Bom != null && FinalNewline != null;

    /// <summary>Values of this format, and for those it does not set, the values of <paramref name="lower"/>.</summary>
    public NewFileFormat Or(NewFileFormat lower) =>
        new(Crlf ?? lower.Crlf, Crlf != null ? CrlfSource : lower.CrlfSource,
            Bom ?? lower.Bom, Bom != null ? BomSource : lower.BomSource,
            FinalNewline ?? lower.FinalNewline, FinalNewline != null ? FinalNewlineSource : lower.FinalNewlineSource,
            NeighborNotUtf8 || lower.NeighborNotUtf8);

    /// <summary>The same format with the BOM set from another rule (non-ASCII content next to legacy files).</summary>
    public NewFileFormat WithBom(bool bom, string source) =>
        new(Crlf, CrlfSource, bom, source, FinalNewline, FinalNewlineSource, NeighborNotUtf8);

    /// <summary>For the log: "crlf:neighbors bom:default final:default".</summary>
    public override string ToString() =>
        $"{Describe(Crlf, "crlf", "lf")}:{CrlfSource ?? "-"} " +
        $"{Describe(Bom, "bom", "nobom")}:{BomSource ?? "-"} " +
        $"{Describe(FinalNewline, "final", "nofinal")}:{FinalNewlineSource ?? "-"}" +
        (NeighborNotUtf8 ? " neighborNotUtf8" : "");

    private static string Describe(bool? value, string whenTrue, string whenFalse) =>
        value == null ? "?" : value.Value ? whenTrue : whenFalse;
}