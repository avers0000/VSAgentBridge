using System.Runtime.InteropServices;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>Finds the Visual Studio instance responsible for a file (spec §4.5a).</summary>
internal static class VsSelector
{
    /// <summary>
    /// For an existing file: asks every instance whether its open solution contains the file
    /// (Solution.FindProjectItem), then lets <see cref="SolutionChooser"/> pick one.
    /// </summary>
    public static VsSelection SelectForExistingFile(
        IReadOnlyList<VsInstance> instances, string filePath, string workingDirectory)
    {
        var checks = instances.Select(vs => Check(vs, filePath)).ToList();
        var candidates = checks.Where(c => c.ContainsFile).ToList();

        var index = SolutionChooser.Choose(
            candidates.Select(c => c.SolutionPath!).ToList(), workingDirectory, out var reason);

        return new VsSelection(checks, index >= 0 ? candidates[index].Instance : null, reason);
    }

    private static VsCandidateCheck Check(VsInstance vs, string filePath)
    {
        try
        {
            var solution = vs.Dte.Solution;
            if (!solution.IsOpen)
                return new VsCandidateCheck(vs, null, false, null);

            var contains = solution.FindProjectItem(filePath) != null;
            return new VsCandidateCheck(vs, solution.FullName, contains, null);
        }
        catch (COMException ex)
        {
            return new VsCandidateCheck(vs, null, false, $"0x{ex.HResult:X8}: {ex.Message}");
        }
    }
}