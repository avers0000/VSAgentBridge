using System.Diagnostics;

namespace VSAgentBridge.Logic;

/// <summary>
/// The format of a new file from its three levels (slice 5 results, §1.2): .editorconfig, then neighbor files,
/// then the Visual Studio default, value by value. One rule on top: "no BOM" taken from neighbors that are not valid
/// UTF-8 (a legacy code page) is replaced by a BOM when the content is not pure ASCII — the bridge writes UTF-8, and
/// without a BOM next to such files it would be read as the legacy code page. An explicit .editorconfig is obeyed.
/// Shared by create_file and by the hook's "after" for a file written with the write tool.
/// </summary>
internal static class NewFileFormatResolver
{
    public const string SourceNonAsciiNextToLegacy = "nonAsciiNextToLegacy";

    /// <param name="filePath">The new file.</param>
    /// <param name="scopeDirectory">Neighbors are not looked for above it (project folder, or solution folder).</param>
    /// <param name="content">The file's text (for the non-ASCII rule).</param>
    /// <param name="elapsedMs">Time spent on disk lookups, for the log.</param>
    public static NewFileFormat Resolve(string filePath, string scopeDirectory, string content, out long elapsedMs)
    {
        var sw = Stopwatch.StartNew();
        var editorConfig = EditorConfigReader.Read(filePath);

        // Neighbors are read only if .editorconfig leaves something they can give.
        var neighbors = editorConfig.Crlf != null && editorConfig.Bom != null
            ? NewFileFormat.None
            : NeighborFormatProbe.Probe(filePath, scopeDirectory);

        var format = Resolve(editorConfig, neighbors, content);
        elapsedMs = sw.ElapsedMilliseconds;
        return format;
    }

    /// <summary>The combination itself, without disk access (unit tests).</summary>
    public static NewFileFormat Resolve(NewFileFormat editorConfig, NewFileFormat neighbors, string content)
    {
        var format = editorConfig.Or(neighbors).Or(NewFileFormat.VisualStudioDefault);

        if (format.Bom == false && format.BomSource == NewFileFormat.SourceNeighbors
            && format.NeighborNotUtf8 && HasNonAscii(content))
            format = format.WithBom(true, SourceNonAsciiNextToLegacy);

        return format;
    }

    private static bool HasNonAscii(string content)
    {
        foreach (var c in content)
            if (c > 127) return true;
        return false;
    }
}