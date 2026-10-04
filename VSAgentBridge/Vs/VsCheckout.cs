using System.Diagnostics;
using System.Runtime.InteropServices;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>
/// Checks a file out through the source control of the selected Visual Studio instance
/// and verifies the result on disk and in Visual Studio.
/// The caller must have a message filter registered on the current STA thread.
/// </summary>
internal static class VsCheckout
{
    // E_PENDING: "data not yet available" (e.g. SSDT still building its model).
    // The message filter retries only RPC_E_CALL_REJECTED, so E_PENDING is retried here.
    private const int EPending = unchecked((int)0x8000000A);
    private const int PendingRetryDelayMs = 500;
    private const int PendingRetryTimeoutMs = 10_000;

    // The read-only attribute is expected to be cleared by the time CheckOutItem returns;
    // a short wait covers the case where it is not. The measured wait goes to the result.
    private const int VerifyPollDelayMs = 100;
    private const int VerifyTimeoutMs = 2_000;

    public static CheckoutResult CheckOut(VsInstance vs, string path)
    {
        var attempts = 0;
        var returned = false;
        try
        {
            var sourceControl = vs.Dte.SourceControl;
            var pending = Stopwatch.StartNew();
            while (true)
            {
                attempts++;
                try
                {
                    returned = sourceControl.CheckOutItem(path);
                    break;
                }
                catch (COMException ex) when (ex.HResult == EPending && pending.ElapsedMilliseconds < PendingRetryTimeoutMs)
                {
                    Thread.Sleep(PendingRetryDelayMs);
                }
            }

            var verify = Stopwatch.StartNew();
            var writable = IsWritable(path);
            while (returned && !writable && verify.ElapsedMilliseconds < VerifyTimeoutMs)
            {
                Thread.Sleep(VerifyPollDelayMs);
                writable = IsWritable(path);
            }
            var waitedMs = verify.ElapsedMilliseconds;

            return new CheckoutResult(returned, attempts, writable, sourceControl.IsItemCheckedOut(path), waitedMs, error: null);
        }
        catch (COMException ex)
        {
            return Failed($"VS {vs.MonikerVersion} pid={vs.ProcessId}: COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        CheckoutResult Failed(string error) =>
            new(returned, attempts, isWritable: false, isCheckedOut: false, verifyWaitMs: 0, error);
    }

    private static bool IsWritable(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0;
}
