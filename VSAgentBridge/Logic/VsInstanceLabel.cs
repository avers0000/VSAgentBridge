namespace VSAgentBridge.Logic;

/// <summary>
/// Label of a Visual Studio instance: a short form for the log ("16.0/pid16616") and a readable form
/// for messages ("VS2019 (pid 16616): App.sln"). The Vs layer builds labels only through <see cref="Format"/>,
/// so the format has a single owner and <see cref="Describe"/> can rely on it.
/// </summary>
internal static class VsInstanceLabel
{
    private const string PidMarker = "/pid";

    /// <summary>Log label from the ROT moniker version (e.g. "17.0") and the process id.</summary>
    public static string Format(string monikerVersion, int processId) => $"{monikerVersion}{PidMarker}{processId}";

    /// <summary>
    /// Readable form for the model and the user: product year instead of the internal version,
    /// plus the file name of the open solution when known. A label in an unexpected format is returned as is.
    /// </summary>
    public static string Describe(string label, string? solutionPath = null)
    {
        var at = label.LastIndexOf(PidMarker, StringComparison.Ordinal);
        var text = at > 0
            ? $"{ProductName(label.Substring(0, at))} (pid {label.Substring(at + PidMarker.Length)})"
            : label;

        return string.IsNullOrEmpty(solutionPath) ? text : $"{text}: {Path.GetFileName(solutionPath)}";
    }

    // The ROT registers DTE under the internal major version; users know the product by its year.
    private static string ProductName(string version) => version switch
    {
        "16.0" => "VS2019",
        "17.0" => "VS2022",
        "18.0" => "VS2026",
        _ => "VS" + version,
    };
}