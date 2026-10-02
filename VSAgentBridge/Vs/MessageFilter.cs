using System.Runtime.InteropServices;

namespace VSAgentBridge.Vs;

/// <summary>
/// COM message filter: retries calls that a busy Visual Studio rejects (RPC_E_CALL_REJECTED).
/// Must be registered on an STA thread; dispose to restore the previous filter.
/// </summary>
internal sealed class MessageFilter : IOleMessageFilter
{
    private const int ServerCallIsHandled = 0;
    private const int ServerCallRetryLater = 2;
    private const int PendingMsgWaitDefProcess = 2;
    private const int RetryDelayMs = 100;
    private const int RetryTimeoutMs = 10_000;

    public static IDisposable Register()
    {
        Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(new MessageFilter(), out var previous));
        return new Registration(previous);
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => ServerCallIsHandled;

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        // Callee is busy: retry after a short delay until the timeout, then cancel the call (-1).
        if (dwRejectType == ServerCallRetryLater && dwTickCount < RetryTimeoutMs)
            return RetryDelayMs;
        return -1;
    }

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => PendingMsgWaitDefProcess;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);

    private sealed class Registration : IDisposable
    {
        private readonly IOleMessageFilter? _previous;
        private bool _disposed;

        public Registration(IOleMessageFilter? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CoRegisterMessageFilter(_previous, out _);
        }
    }
}

[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);
    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}