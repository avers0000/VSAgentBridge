namespace VSAgentBridge.Vs;

/// <summary>Which Visual Studio instance was selected for a file, and why.</summary>
internal sealed class VsSelection
{
    public VsSelection(IReadOnlyList<VsCandidateCheck> checks, VsInstance? selected, string reason)
    {
        Checks = checks;
        Selected = selected;
        Reason = reason;
    }

    public IReadOnlyList<VsCandidateCheck> Checks { get; }

    public VsInstance? Selected { get; }

    public string Reason { get; }
}