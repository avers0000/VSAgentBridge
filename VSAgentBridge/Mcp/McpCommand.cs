using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VSAgentBridge.Hook;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Mcp;

/// <summary>
/// "vsagentbridge mcp": the MCP server "vsab" over stdio with one tool, create_file (slice 5).
/// One process per harness instance, alive while the harness keeps stdin open. stdout belongs to the protocol:
/// nothing else may write to it (diagnostics go to stderr and the log).
/// The server has no agent session: whatever needs one (Output, the session rule) stays with the hook (§1.1).
/// </summary>
internal static class McpCommand
{
    public static int Run()
    {
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var settings = BridgeSettings.Load(exeDirectory, out var settingsWarning);
        var outputDir = settings.ResolveOutputDir(exeDirectory);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

        LogStart(settings, settingsWarning, version);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = McpMessages.ServerName, Version = version },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) =>
                    new ValueTask<ListToolsResult>(new ListToolsResult { Tools = new List<Tool> { CreateFileTool.Definition } }),

                // Blocks a thread-pool thread for the duration of the call (up to a few seconds while Visual Studio
                // adds the file): the work itself runs on its own STA thread inside CreateFileTool.
                CallToolHandler = (request, _) =>
                    new ValueTask<CallToolResult>(Call(request.Params, settings, outputDir)),
            },
        };

        var transport = new StdioServerTransport(options);
        var server = McpServer.Create(transport, options);

        // Main is [STAThread]; waiting here is fine: the SDK's continuations run on the thread pool
        // (a console process has no synchronization context).
        server.RunAsync().GetAwaiter().GetResult();
        return 0;
    }

    private static CallToolResult Call(CallToolRequestParams? request, BridgeSettings settings, string outputDir)
    {
        if (request == null || request.Name != McpMessages.CreateFileTool)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = $"Unknown tool '{request?.Name}'." } },
                IsError = true,
            };
        }

        return CreateFileTool.Call(request.Arguments, settings, outputDir);
    }

    // The folder relative paths are resolved against (slice 5 results, §1.3): checked once in step A.
    private static void LogStart(BridgeSettings settings, string? settingsWarning, string version)
    {
        var log = new HookLog();
        log.Add("mode", "mcp");
        log.Add("event", "start");
        log.Add("version", version);
        log.Add("cwd", Environment.CurrentDirectory);
        log.Add("settingsWarning", settingsWarning);
        log.Write(settings, settingsWarning != null ? LogVerbosity.Issues : LogVerbosity.Actions);
    }
}