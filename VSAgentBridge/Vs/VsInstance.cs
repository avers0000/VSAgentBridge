using EnvDTE;

namespace VSAgentBridge.Vs;

/// <summary>A running Visual Studio instance found in the ROT.</summary>
internal sealed class VsInstance
{
    public VsInstance(string monikerVersion, int processId, DTE dte)
    {
        MonikerVersion = monikerVersion;
        ProcessId = processId;
        Dte = dte;
    }

    /// <summary>Version from the ROT name, e.g. "17.0" (VS 2022), "18.0" (VS 2026).</summary>
    public string MonikerVersion { get; }

    public int ProcessId { get; }

    public DTE Dte { get; }
}