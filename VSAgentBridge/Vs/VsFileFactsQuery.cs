using System.Runtime.InteropServices;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Asks the selected Visual Studio instance about a file and packs the answer into VsFileFacts.
/// The caller must have a message filter registered on the current STA thread.
/// </summary>
internal static class VsFileFactsQuery
{
    public static VsFileFacts Query(VsSelection selection, string path)
    {
        var vs = selection.Selected;
        if (vs == null)
            return VsFileFacts.NotFound;

        try
        {
            var sourceControl = vs.Dte.SourceControl;
            return new VsFileFacts(
                instanceFound: true,
                isUnderScc: sourceControl.IsItemUnderSCC(path),
                isCheckedOut: sourceControl.IsItemCheckedOut(path),
                error: null);
        }
        catch (COMException ex)
        {
            // Typical: RPC_E_CALL_REJECTED after the filter gave up (VS busy),
            // RPC_S_SERVER_UNAVAILABLE 0x800706BA (VS closed since the ROT scan).
            return VsFileFacts.Failed($"VS {vs.MonikerVersion} pid={vs.ProcessId}: COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
    }
}