using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Diag;

/// <summary>"vsagentbridge diag [--all] [file]": diagnostic output, no side effects.</summary>
internal static class DiagCommand
{
    public static int Run(string[] args)
    {
        var showAll = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));
        var fileArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var total = Stopwatch.StartNew();

        Console.WriteLine($"Process elevated: {(IsElevated() ? "yes" : "no")}");

        using var filter = MessageFilter.Register();

        var instances = RunningObjectTable.GetVisualStudioInstances();
        PrintInstances(instances, total.ElapsedMilliseconds);

        if (fileArg != null)
            PrintFile(instances, fileArg);

        if (showAll)
        {
            Console.WriteLine();
            Console.WriteLine("All ROT entries:");
            foreach (var name in RunningObjectTable.GetDisplayNames())
                Console.WriteLine($"  {name}");
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {total.ElapsedMilliseconds} ms");
        return 0;
    }

    private static void PrintInstances(IReadOnlyList<VsInstance> instances, long scanMs)
    {
        Console.WriteLine($"Visual Studio instances: {instances.Count}  (ROT scan {scanMs} ms)");
        if (instances.Count == 0)
        {
            Console.WriteLine("  Hint: a process sees only Visual Studio instances running at the same elevation level.");
            Console.WriteLine("        Check that Visual Studio and vsagentbridge are both elevated (or both not).");
            return;
        }

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
    }

    private static void PrintFile(IReadOnlyList<VsInstance> instances, string fileArg)
    {
        var cwd = Environment.CurrentDirectory;
        var path = PathUtil.ToFullPath(fileArg, cwd);

        Console.WriteLine();
        Console.WriteLine($"File: {path}");
        if (File.Exists(path))
        {
            var readOnly = File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
            Console.WriteLine($"  Exists, read-only: {(readOnly ? "yes" : "no")}");
        }
        else
        {
            Console.WriteLine("  Does not exist");
        }

        var sw = Stopwatch.StartNew();
        var selection = VsSelector.SelectForExistingFile(instances, path, cwd);
        Console.WriteLine($"  Instance checks ({sw.ElapsedMilliseconds} ms):");
        foreach (var check in selection.Checks)
        {
            var state = check.Error != null ? $"ERROR {check.Error}"
                : check.SolutionPath == null ? "no solution"
                : $"{(check.ContainsFile ? "CONTAINS" : "-       ")}  {check.SolutionPath}";
            Console.WriteLine($"    VS {check.Instance.MonikerVersion} pid={check.Instance.ProcessId}: {state}");
        }

        var selected = selection.Selected;
        Console.WriteLine(selected != null
            ? $"  Selected: VS {selected.MonikerVersion} pid={selected.ProcessId}"
            : "  Selected: none");
        Console.WriteLine($"  Reason  : {selection.Reason}");

        if (selected == null)
            return;

        sw.Restart();
        try
        {
            var scc = selected.Dte.SourceControl;
            Console.WriteLine($"  IsItemUnderSCC   : {scc.IsItemUnderSCC(path)}");
            Console.WriteLine($"  IsItemCheckedOut : {scc.IsItemCheckedOut(path)}");
        }
        catch (COMException ex)
        {
            Console.WriteLine($"  SourceControl ERROR 0x{ex.HResult:X8}: {ex.Message}");
        }
        Console.WriteLine($"  ({sw.ElapsedMilliseconds} ms)");
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}