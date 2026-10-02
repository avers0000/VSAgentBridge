namespace VSAgentBridge.Logic;

/// <summary>
/// Chooses one solution among several that contain the same file (spec §4.5a).
/// Pure logic: works on solution paths only, no COM.
/// </summary>
internal static class SolutionChooser
{
    /// <summary>
    /// Returns the index of the chosen solution in <paramref name="solutionPaths"/>, or -1 if the list is empty.
    /// Rule: a single candidate wins; with several, prefer the one whose folder contains
    /// <paramref name="workingDirectory"/>; otherwise take the first.
    /// </summary>
    public static int Choose(IReadOnlyList<string> solutionPaths, string workingDirectory, out string reason)
    {
        switch (solutionPaths.Count)
        {
            case 0:
                reason = "no open solution contains the file";
                return -1;
            case 1:
                reason = "the only open solution that contains the file";
                return 0;
        }

        for (var i = 0; i < solutionPaths.Count; i++)
        {
            var solutionFolder = Path.GetDirectoryName(solutionPaths[i]);
            if (solutionFolder != null && PathUtil.IsUnder(workingDirectory, solutionFolder))
            {
                reason = $"{solutionPaths.Count} candidates; solution folder contains the working directory";
                return i;
            }
        }

        reason = $"{solutionPaths.Count} candidates; none contains the working directory, first one taken";
        return 0;
    }
}