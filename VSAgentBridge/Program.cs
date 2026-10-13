using VSAgentBridge.Diag;
using VSAgentBridge.Hook;
using VSAgentBridge.Init;
using VSAgentBridge.Mcp;

namespace VSAgentBridge;

internal static class Program
{
    // STA is required for COM calls to Visual Studio (message filter).
    // The MCP server waits here and runs each tool call on its own STA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "diag", StringComparison.OrdinalIgnoreCase))
            return DiagCommand.Run(args.Skip(1).ToArray());

        if (args.Length > 0 && string.Equals(args[0], "hook", StringComparison.OrdinalIgnoreCase))
            return HookCommand.Run();

        if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
            return McpCommand.Run();

        if (args.Length > 0 && string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
            return InitCommand.Run(args.Skip(1).ToArray());

        Console.Error.WriteLine("Usage: vsagentbridge diag [--all] [--checkout] [file]");
        Console.Error.WriteLine("       vsagentbridge hook    (one JSON request on stdin, one JSON response on stdout)");
        Console.Error.WriteLine("       vsagentbridge mcp     (MCP server \"vsab\" over stdio: tool create_file)");
        Console.Error.WriteLine("       vsagentbridge init claude|opencode [--roots \"D:\\A;D:\\B\"] [-g] [--no-marketplace]");
        Console.Error.WriteLine("                             (set up an agent to use the bridge; asks before changing anything)");
        return 2;
    }
}