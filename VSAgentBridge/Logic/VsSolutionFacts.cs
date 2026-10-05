namespace VSAgentBridge.Logic;

/// <summary>
/// What one Visual Studio instance reported about its open solution, for a new file at a given path.
/// Filled in by the Vs layer; plain data, no COM. The decider refers to instances by their index
/// in the list it receives; the Vs layer maps the index back to the instance.
/// </summary>
internal sealed class VsSolutionFacts
{
    public VsSolutionFacts(
        string instance,
        string? solutionPath,
        IReadOnlyList<VsProjectFacts> projects,
        bool containsPath,
        string? error)
    {
        Instance = instance;
        SolutionPath = solutionPath;
        Projects = projects;
        ContainsPath = containsPath;
        Error = error;
    }

    /// <summary>No solution is open in this instance.</summary>
    public static VsSolutionFacts NoSolution(string instance) =>
        new(instance, solutionPath: null, Array.Empty<VsProjectFacts>(), containsPath: false, error: null);

    /// <summary>The instance did not answer; what it has open is unknown.</summary>
    public static VsSolutionFacts Failed(string instance, string error) =>
        new(instance, solutionPath: null, Array.Empty<VsProjectFacts>(), containsPath: false, error);

    /// <summary>Instance label for messages and the log, e.g. "17.0/pid12345".</summary>
    public string Instance { get; }

    /// <summary>Full path of the open .sln/.slnx; null when no solution is open or the instance failed.</summary>
    public string? SolutionPath { get; }

    /// <summary>All projects of the solution, including those nested in solution folders and unloaded ones.</summary>
    public IReadOnlyList<VsProjectFacts> Projects { get; }

    /// <summary>
    /// Solution.FindProjectItem found the path: a project already lists it
    /// (an item whose file is missing, or a wildcard of an SDK-style project).
    /// </summary>
    public bool ContainsPath { get; }

    /// <summary>Set when the instance could not be asked; the other values are then meaningless.</summary>
    public string? Error { get; }

    /// <summary>The instance answered and has a solution open.</summary>
    public bool IsOpen => Error == null && SolutionPath != null;
}