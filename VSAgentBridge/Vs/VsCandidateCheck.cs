namespace VSAgentBridge.Vs;

/// <summary>Result of checking one Visual Studio instance for a given file.</summary>
internal sealed class VsCandidateCheck
{
    public VsCandidateCheck(VsInstance instance, string? solutionPath, bool containsFile, string? error)
    {
        Instance = instance;
        SolutionPath = solutionPath;
        ContainsFile = containsFile;
        Error = error;
    }

    public VsInstance Instance { get; }

    /// <summary>Full path of the open solution; null if no solution is open or the check failed.</summary>
    public string? SolutionPath { get; }

    public bool ContainsFile { get; }

    /// <summary>COM error text if the instance did not answer; otherwise null.</summary>
    public string? Error { get; }
}