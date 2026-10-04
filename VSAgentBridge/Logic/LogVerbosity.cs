namespace VSAgentBridge.Logic;

/// <summary>
/// How much the bridge writes to its log. Each level includes everything the levels above it write.
/// Configured as "logLevel" in vsagentbridge.json.
/// </summary>
internal enum LogVerbosity
{
    /// <summary>Nothing.</summary>
    Off = 0,

    /// <summary>Only issues: denials, failed checkouts, internal errors, invalid requests, settings warnings.</summary>
    Issues = 1,

    /// <summary>Issues plus calls where the bridge changed something through Visual Studio (successful checkout).</summary>
    Actions = 2,

    /// <summary>Every call, including edits of writable files that pass through untouched.</summary>
    All = 3,
}