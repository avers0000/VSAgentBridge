using System.Text.RegularExpressions;

namespace VSAgentBridge.Logic;

/// <summary>
/// The version an agent CLI prints for "--version" ("2.1.292 (Claude Code)", "opencode v2.0.6"),
/// and the minimum versions the bridge needs. Pure string logic: the command is run by the caller.
/// </summary>
internal static class HarnessVersion
{
    /// <summary>Claude Code: the bridge plugin is a mod, and mods need 2.1.287.</summary>
    public static readonly Version ClaudeMinimum = new(2, 1, 287);

    /// <summary>OpenCode: the 2.x configuration format (mcp.servers).</summary>
    public static readonly Version OpenCodeMinimum = new(2, 0, 0);

    // The first "N.N" or "N.N.N" that is not part of a longer number, optionally after "v".
    // Anything after it ("-beta", " (Claude Code)") is ignored.
    private static readonly Regex VersionPattern =
        new(@"(?<![\d.])v?(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The version in <paramref name="output"/>, always with three parts ("2.0" becomes 2.0.0),
    /// or null if there is none.
    /// </summary>
    public static Version? Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        var match = VersionPattern.Match(output);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor))
            return null;

        var build = 0;
        if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out build))
            return null;

        return new Version(major, minor, build);
    }

    /// <summary>True if <paramref name="output"/> has a version and it is at least <paramref name="minimum"/>.</summary>
    public static bool IsAtLeast(string? output, Version minimum, out Version? version)
    {
        version = Parse(output);
        return version != null && version >= minimum;
    }
}