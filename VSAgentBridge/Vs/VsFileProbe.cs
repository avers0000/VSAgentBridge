using System.Diagnostics;
using System.Runtime.InteropServices;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Lazy Visual Studio lookup for one file: <see cref="Query"/> scans the ROT, selects the instance
/// and asks it about the file. Passed to EditDecider as its callback, so Visual Studio is touched
/// only when the decision needs it. Keeps the selection for the following checkout and the log.
/// The caller must have a message filter registered on the current STA thread.
/// </summary>
internal sealed class VsFileProbe
{
    private readonly string _path;
    private readonly string _workingDirectory;

    public VsFileProbe(string path, string workingDirectory)
    {
        _path = path;
        _workingDirectory = workingDirectory;
    }

    /// <summary>Null until <see cref="Query"/> ran, or if the ROT scan failed.</summary>
    public VsSelection? Selection { get; private set; }

    public VsInstance? SelectedInstance => Selection?.Selected;

    public int InstanceCount { get; private set; }

    /// <summary>ROT scan + instance selection + SourceControl queries.</summary>
    public long QueryMs { get; private set; }

    public VsFileFacts Query()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var instances = RunningObjectTable.GetVisualStudioInstances();
            InstanceCount = instances.Count;
            Selection = VsSelector.SelectForExistingFile(instances, _path, _workingDirectory);
            return VsFileFactsQuery.Query(Selection, _path);
        }
        catch (COMException ex)
        {
            return VsFileFacts.Failed($"ROT: COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
        finally
        {
            QueryMs = sw.ElapsedMilliseconds;
        }
    }
}