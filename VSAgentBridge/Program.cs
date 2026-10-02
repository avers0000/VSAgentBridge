using VSAgentBridge.Diag;

namespace VSAgentBridge;

internal static class Program
{
    // STA is required for COM calls to Visual Studio (message filter in later steps).
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "diag", StringComparison.OrdinalIgnoreCase))
            return DiagCommand.Run(args.Skip(1).ToArray());

        Console.Error.WriteLine("Usage: vsagentbridge diag [--all]");
        return 2;
    }
}