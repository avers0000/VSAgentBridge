using System.ComponentModel;
using System.Reflection;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Init;

/// <summary>
/// "vsagentbridge init claude|opencode": the common steps (options, the agent CLI and its version, workspace roots,
/// the rules question), then the agent's own part (<see cref="ClaudeInit"/>, <see cref="OpenCodeInit"/>).
/// Exit codes: 0 done or declined, 1 error or stopped, 2 wrong options.
/// </summary>
internal static class InitCommand
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    public static int Run(string[] args) =>
        Run(args, ConsolePrompt.ForConsole(), AppDomain.CurrentDomain.BaseDirectory);

    internal static int Run(IReadOnlyList<string> args, ConsolePrompt prompt, string installDirectory)
    {
        var output = prompt.Output;
        if (!InitOptions.TryParse(args, out var options, out var error))
        {
            output.WriteLine(error);
            output.WriteLine();
            output.WriteLine("Usage:");
            foreach (var line in InitOptions.Usage.Split('\n'))
                output.WriteLine("  " + line);
            return 2;
        }

        installDirectory = Path.GetFullPath(installDirectory).TrimEnd('\\');
        var settings = BridgeSettings.Load(installDirectory, out var settingsWarning);
        var outputDirectory = settings.ResolveOutputDir(installDirectory);

        var isClaude = options!.Harness == InitOptions.Claude;
        var agentName = isClaude ? "Claude Code" : "OpenCode";
        output.WriteLine($"VS Agent Bridge {BridgeVersion()} - setup for {agentName}");
        output.WriteLine($"Install folder: {installDirectory}");
        if (settingsWarning != null)
            output.WriteLine("WARNING: " + settingsWarning);
        output.WriteLine();

        // ---- the agent CLI ----
        var command = ExternalCommand.Find(options.Harness);
        if (command == null)
        {
            output.WriteLine($"{agentName} ({options.Harness}) was not found in PATH. Install it first; nothing was changed.");
            return 1;
        }

        var minimum = isClaude ? HarnessVersion.ClaudeMinimum : HarnessVersion.OpenCodeMinimum;
        if (!CheckVersion(output, command, agentName, minimum))
            return 1;

        // ---- workspace roots and rules ----
        ClaudeInit? claude = null;
        string? currentRoots = null;
        if (isClaude)
        {
            if (!ClaudeInit.TryLoad(prompt, command, installDirectory, outputDirectory, out claude, out var loadError))
            {
                output.WriteLine(loadError);
                return 1;
            }
            currentRoots = claude!.CurrentRoots;
        }

        var roots = GetRoots(prompt, options.Roots, currentRoots);
        if (roots == null)
            return 1;

        var rulesPath = isClaude ? claude!.RulesPath : OpenCodeInit.RulesPath();
        var globalRules = options.GlobalRules || prompt.AskYesNo($"Install the rules globally ({rulesPath})?");

        return isClaude
            ? claude!.Run(roots, globalRules, options.NoMarketplace)
            : OpenCodeInit.Run(prompt, installDirectory, outputDirectory, roots, globalRules);
    }

    private static bool CheckVersion(TextWriter output, ExternalCommand command, string agentName, Version minimum)
    {
        (int ExitCode, string Output, string Error, bool TimedOut) result;
        try
        {
            result = command.Run(new[] { "--version" }, VersionTimeout);
        }
        catch (Win32Exception ex)
        {
            output.WriteLine($"{command.Path} could not be started: {ex.Message}. Nothing was changed.");
            return false;
        }

        var printed = (result.Output + " " + result.Error).Trim();
        if (result.TimedOut || result.ExitCode != 0)
        {
            output.WriteLine($"\"{command.Name} --version\" failed ({(result.TimedOut ? "no answer" : "exit code " + result.ExitCode)}): " +
                             $"{printed}. Nothing was changed.");
            return false;
        }

        if (!HarnessVersion.IsAtLeast(printed, minimum, out var version))
        {
            output.WriteLine(version == null
                ? $"Could not read the version of {agentName} from \"{printed}\". Nothing was changed."
                : $"{agentName} {version} is too old: {minimum} or later is needed. Update it first; nothing was changed.");
            return false;
        }

        output.WriteLine($"{agentName} {version}: {command.Path}");
        return true;
    }

    // --roots is checked without asking; otherwise asks until the answer is valid. Null: give up (error or end of input).
    private static IReadOnlyList<string>? GetRoots(ConsolePrompt prompt, string? fromOptions, string? current)
    {
        var output = prompt.Output;
        if (fromOptions != null)
        {
            if (TryRoots(fromOptions, out var roots, out var error))
                return roots;
            output.WriteLine(error);
            return null;
        }

        output.WriteLine();
        output.WriteLine("Workspace roots: the folders where the bridge works (your solutions are in them), separated by ';'.");
        if (current != null)
            output.WriteLine("Press Enter to keep the current ones, or type the new list.");

        while (true)
        {
            var answer = prompt.AskLine(current != null ? $"Workspace roots [{current}]:" : "Workspace roots:");
            if (answer == null)
            {
                output.WriteLine();
                return null;
            }

            if (answer.Length == 0 && current != null)
                answer = current;

            if (TryRoots(answer, out var roots, out var error))
                return roots;
            output.WriteLine(error);
        }
    }

    private static bool TryRoots(string input, out IReadOnlyList<string> roots, out string? error)
    {
        if (!WorkspaceRoots.TryParse(input, out roots, out error))
            return false;

        var missing = roots.Where(r => !Directory.Exists(r)).ToList();
        if (missing.Count == 0)
            return true;

        error = "Folder not found: " + string.Join(", ", missing.Select(m => $"\"{m}\""));
        roots = Array.Empty<string>();
        return false;
    }

    // "0.0.0-dev+53242b5..." -> "0.0.0-dev": the commit is for bug reports, not for this header.
    private static string BridgeVersion()
    {
        var version = typeof(InitCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(InitCommand).Assembly.GetName().Version.ToString();
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version.Substring(0, plus);
    }
}