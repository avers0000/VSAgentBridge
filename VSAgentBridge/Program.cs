using VSAgentBridge.Diag;
using VSAgentBridge.Hook;

namespace VSAgentBridge;

internal static class Program
{
    // STA is required for COM calls to Visual Studio (message filter).
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "diag", StringComparison.OrdinalIgnoreCase))
            return DiagCommand.Run(args.Skip(1).ToArray());

        if (args.Length > 0 && string.Equals(args[0], "hook", StringComparison.OrdinalIgnoreCase))
            return HookCommand.Run();

        Console.Error.WriteLine("Usage: vsagentbridge diag [--all] [--checkout] [file]");
        Console.Error.WriteLine("       vsagentbridge hook    (one JSON request on stdin, one JSON response on stdout)");
        return 2;
    }
}