using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Vs;

/// <summary>Reads the COM Running Object Table (ROT) of the current user session.</summary>
internal static class RunningObjectTable
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

    /// <summary>Returns display names of all monikers currently registered in the ROT.</summary>
    public static List<string> GetDisplayNames()
    {
        var result = new List<string>();
        ForEachEntry((_, _, name) => result.Add(name));
        return result;
    }

    /// <summary>Returns all Visual Studio instances that publish their DTE object in the ROT.</summary>
    public static List<VsInstance> GetVisualStudioInstances()
    {
        var result = new List<VsInstance>();
        ForEachEntry((rot, moniker, name) =>
        {
            if (!VsMonikerName.TryParse(name, out var version, out var pid))
                return;

            if (rot.GetObject(moniker, out var obj) == 0 && obj is DTE dte)
                result.Add(new VsInstance(version, pid, dte));
        });
        return result;
    }

    private static void ForEachEntry(Action<IRunningObjectTable, IMoniker, string> action)
    {
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out var rot));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out var ctx));
        try
        {
            rot.EnumRunning(out var enumMoniker);
            try
            {
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        moniker.GetDisplayName(ctx, null, out var name);
                        action(rot, moniker, name);
                    }
                    catch (COMException)
                    {
                        // Some ROT entries refuse to provide a display name; skip them.
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumMoniker);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(ctx);
            Marshal.ReleaseComObject(rot);
        }
    }
}