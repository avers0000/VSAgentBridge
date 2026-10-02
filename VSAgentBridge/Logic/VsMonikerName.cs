namespace VSAgentBridge.Logic;

/// <summary>
/// Parses ROT display names of Visual Studio DTE objects,
/// e.g. "!VisualStudio.DTE.17.0:12345" (version 17.0, process id 12345).
/// </summary>
internal static class VsMonikerName
{
    private const string Prefix = "!VisualStudio.DTE.";

    public static bool TryParse(string displayName, out string version, out int processId)
    {
        version = "";
        processId = 0;

        if (!displayName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = displayName.Substring(Prefix.Length);
        var colon = rest.LastIndexOf(':');
        if (colon <= 0)
            return false;

        if (!int.TryParse(rest.Substring(colon + 1), out processId))
            return false;

        version = rest.Substring(0, colon);
        return true;
    }
}