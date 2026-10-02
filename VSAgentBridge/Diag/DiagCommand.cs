using System.Diagnostics;
using System.Runtime.InteropServices;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Diag;

/// <summary>"vsagentbridge diag": diagnostic output, no side effects.</summary>
internal static class DiagCommand
{
    public static int Run(string[] args)
    {
        var showAll = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));
        var total = Stopwatch.StartNew();

        using var filter = MessageFilter.Register();

        var instances = RunningObjectTable.GetVisualStudioInstances();
        Console.WriteLine($"Visual Studio instances: {instances.Count}  (ROT scan {total.ElapsedMilliseconds} ms)");

        foreach (var vs in instances)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"  VS {vs.MonikerVersion}  pid={vs.ProcessId}");
            try
            {
                Console.WriteLine($"    DTE.Version : {vs.Dte.Version}");
                var solution = vs.Dte.Solution;
                Console.WriteLine(solution.IsOpen
                    ? $"    Solution    : {solution.FullName}"
                    : "    Solution    : (none)");
            }
            catch (COMException ex)
            {
                Console.WriteLine($"    ERROR 0x{ex.HResult:X8}: {ex.Message}");
            }
            Console.WriteLine($"    ({sw.ElapsedMilliseconds} ms)");
        }

        if (showAll)
        {
            Console.WriteLine("All ROT entries:");
            foreach (var name in RunningObjectTable.GetDisplayNames())
                Console.WriteLine($"  {name}");
        }

        Console.WriteLine($"Total: {total.ElapsedMilliseconds} ms");
        return 0;
    }
}