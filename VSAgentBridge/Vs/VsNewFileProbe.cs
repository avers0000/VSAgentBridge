using System.Diagnostics;
using System.Runtime.InteropServices;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Lazy Visual Studio lookup for a new file: <see cref="Query"/> scans the ROT and asks every instance
/// about its open solution. Passed to NewFileDecider as its callback, so Visual Studio is touched only for
/// a path in a TFVC server workspace. Keeps the instances so that the decision's index maps back to one.
/// The caller must have a message filter registered on the current STA thread.
/// </summary>
internal sealed class VsNewFileProbe
{
    private readonly string _path;
    private IReadOnlyList<VsInstance> _instances = Array.Empty<VsInstance>();

    public VsNewFileProbe(string path)
    {
        _path = path;
    }

    /// <summary>Number of Visual Studio instances found in the ROT; 0 until <see cref="Query"/> ran.</summary>
    public int InstanceCount => _instances.Count;

    /// <summary>ROT scan + questions to every instance.</summary>
    public long QueryMs { get; private set; }

    public IReadOnlyList<VsSolutionFacts> Query()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _instances = RunningObjectTable.GetVisualStudioInstances();
            return VsSolutionQuery.Query(_instances, _path);
        }
        catch (COMException ex)
        {
            // The ROT itself could not be read: report it as one instance that did not answer,
            // so the decision is a denial with a visible cause rather than "no open solution".
            _instances = Array.Empty<VsInstance>();
            return new[] { VsSolutionFacts.Failed("ROT", $"COMException 0x{ex.HResult:X8}: {ex.Message}") };
        }
        finally
        {
            QueryMs = sw.ElapsedMilliseconds;
        }
    }

    /// <summary>The instance behind an index of the decision (NewFileDecision.SolutionIndex); null for -1.</summary>
    public VsInstance? InstanceAt(int index) =>
        index >= 0 && index < _instances.Count ? _instances[index] : null;
}