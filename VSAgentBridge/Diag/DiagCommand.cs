using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VSAgentBridge.Logic;
using VSAgentBridge.Vs;

namespace VSAgentBridge.Diag;

/// <summary>
/// "vsagentbridge diag [--all] [--checkout] [file]": diagnostic output.
/// Changes nothing unless --checkout is given and the decision for the file is Checkout.
/// </summary>
internal static class DiagCommand
{
    public static int Run(string[] args)
    {
        var showAll = HasFlag(args, "--all");
        var checkout = HasFlag(args, "--checkout");
        var fileArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var total = Stopwatch.StartNew();

        Console.WriteLine($"Process elevated: {(IsElevated() ? "yes" : "no")}");

        using var filter = MessageFilter.Register();

        var instances = RunningObjectTable.GetVisualStudioInstances();
        PrintInstances(instances, total.ElapsedMilliseconds);

        var cacheFiles = PrintTfvcCache();

        if (fileArg != null)
            PrintFile(instances, fileArg, cacheFiles, checkout);

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

    private static void PrintFile(IReadOnlyList<VsInstance> instances, string fileArg, IReadOnlyList<TfvcCacheFile> cacheFiles, bool checkout)
    {
        var cwd = Environment.CurrentDirectory;
        var path = PathUtil.ToFullPath(fileArg, cwd);

        Console.WriteLine();
        Console.WriteLine($"File: {path}");
        var exists = File.Exists(path);
        var readOnly = exists && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        Console.WriteLine(exists ? $"  Exists, read-only: {(readOnly ? "yes" : "no")}" : "  Does not exist");

        var tfvc = TfvcPathClassifier.Classify(path, Environment.MachineName, cacheFiles);
        Console.WriteLine($"  TFVC cache: {tfvc.Status}");
        if (tfvc.Workspace != null)
            Console.WriteLine($"    workspace '{tfvc.Workspace.Name}', root {tfvc.MappedRoot}, from {tfvc.CacheFilePath}");

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

        sw.Restart();
        var decision = EditDecider.Decide(exists, readOnly, tfvc, () => VsFileFactsQuery.Query(selection, path));
        PrintDecision(decision, sw.ElapsedMilliseconds);

        if (!checkout)
            return;

        if (decision.Action != EditAction.Checkout)
        {
            Console.WriteLine($"  Checkout: skipped, decision is {decision.Action}");
            return;
        }

        // Decision Checkout implies VS was asked and an instance was selected.
        sw.Restart();
        var result = VsCheckout.CheckOut(selection.Selected!, path);
        PrintCheckout(result, sw.ElapsedMilliseconds);
    }

    private static IReadOnlyList<TfvcCacheFile> PrintTfvcCache()
    {
        var sw = Stopwatch.StartNew();
        var files = TfvcCacheReader.ReadAll();

        Console.WriteLine();
        Console.WriteLine($"TFVC workspace cache: {files.Count} file(s)  ({sw.ElapsedMilliseconds} ms)  machine={Environment.MachineName}");
        foreach (var file in files)
        {
            Console.WriteLine($"  {file.FilePath}");
            Console.WriteLine($"    modified {file.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            if (file.Error != null)
            {
                Console.WriteLine($"    ERROR {file.Error}");
                continue;
            }

            foreach (var ws in file.Workspaces)
            {
                var other = string.Equals(ws.Computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    ? "" : "  (other computer)";
                Console.WriteLine($"    workspace '{ws.Name}'  computer={ws.Computer}  local={ws.IsLocalWorkspace}{other}");
                foreach (var mapped in ws.MappedPaths)
                    Console.WriteLine($"      {mapped}");
            }
        }

        return files;
    }

    private static void PrintDecision(EditDecision decision, long elapsedMs)
    {
        Console.WriteLine($"  Decision: {decision.Action}  ({decision.Reason})  ({elapsedMs} ms)");

        var vs = decision.Vs;
        if (vs == null)
            Console.WriteLine("    Visual Studio not asked");
        else if (vs.Error != null)
            Console.WriteLine($"    VS error: {vs.Error}");
        else
            Console.WriteLine($"    VS: found={vs.InstanceFound}  underScc={vs.IsUnderScc}  checkedOut={vs.IsCheckedOut}");
    }

    private static void PrintCheckout(CheckoutResult result, long elapsedMs)
    {
        Console.WriteLine($"  Checkout: {(result.Succeeded ? "OK" : "FAILED")}  ({elapsedMs} ms)");
        Console.WriteLine($"    CheckOutItem returned {result.CheckOutItemReturned}, attempts {result.Attempts}");
        Console.WriteLine($"    After: writable={result.IsWritable}  checkedOut={result.IsCheckedOut}  (verify wait {result.VerifyWaitMs} ms)");
        if (result.Error != null)
            Console.WriteLine($"    Error: {result.Error}");
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}