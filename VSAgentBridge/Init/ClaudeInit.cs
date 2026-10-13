using System.Text.Json.Nodes;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Init;

/// <summary>
/// "vsagentbridge init claude" after the common steps: the plan, "Apply?", the claude plugin commands,
/// the entries init writes itself in settings.json, the global rules and the summary.
/// </summary>
internal sealed class ClaudeInit
{
    public const string HarnessFolderName = "claude";

    private enum Method
    {
        Marketplace,
        PluginDirs,
        Manual,
    }

    private readonly ConsolePrompt _prompt;
    private readonly ExternalCommand _claude;
    private readonly string _installDirectory;
    private readonly string _outputDirectory;
    private readonly JsonFile _settings;

    private ClaudeInit(ConsolePrompt prompt, ExternalCommand claude, string installDirectory, string outputDirectory,
        string configDirectory, JsonFile settings)
    {
        _prompt = prompt;
        _claude = claude;
        _installDirectory = installDirectory;
        _outputDirectory = outputDirectory;
        SettingsPath = Path.Combine(configDirectory, "settings.json");
        RulesPath = Path.Combine(configDirectory, "CLAUDE.md");
        _settings = settings;
    }

    public string SettingsPath { get; }

    public string RulesPath { get; }

    /// <summary>The workspace roots in settings.json now (marketplace plugin first), or null.</summary>
    public string? CurrentRoots => ClaudeSettingsEditor.GetWorkspaceRoots(_settings.Root);

    private string PluginDirectory => Path.Combine(_installDirectory, HarnessFolderName);

    /// <summary>Claude Code's user folder: CLAUDE_CONFIG_DIR if set, otherwise %USERPROFILE%\.claude.</summary>
    public static string ConfigDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : custom!.Trim();
    }

    /// <summary>Reads settings.json (a missing file is an empty one). False with a message if it cannot be used.</summary>
    public static bool TryLoad(ConsolePrompt prompt, ExternalCommand claude, string installDirectory, string outputDirectory,
        out ClaudeInit? init, out string? error)
    {
        init = null;
        var configDirectory = ConfigDirectory();
        var settingsPath = Path.Combine(configDirectory, "settings.json");
        if (!JsonFile.TryParse(SettingsBackup.ReadIfExists(settingsPath), out var settings, out var parseError))
        {
            error = $"{settingsPath}: {parseError}. Fix the file and run init again; nothing was changed.";
            return false;
        }

        init = new ClaudeInit(prompt, claude, installDirectory, outputDirectory, configDirectory, settings!);
        error = null;
        return true;
    }

    /// <summary>Plans, asks "Apply?", applies, writes the summary. Returns the exit code.</summary>
    public int Run(IReadOnlyList<string> roots, bool globalRules, bool noMarketplace)
    {
        var output = _prompt.Output;
        var rootsText = WorkspaceRoots.Format(roots);
        var method = noMarketplace ? Method.PluginDirs : Method.Marketplace;

        // ---- plan: nothing is changed here ----
        var changes = new List<string>();
        var warnings = new List<string>();

        if (method == Method.Marketplace)
        {
            var current = ClaudeSettingsEditor.GetMarketplacePath(_settings.Root);
            if (current == null)
                changes.Add($"Add the plugin marketplace \"{ClaudeSettingsEditor.MarketplaceName}\": {PluginDirectory}");
            else if (!PathUtil.SamePath(current, PluginDirectory))
                changes.Add($"Marketplace \"{ClaudeSettingsEditor.MarketplaceName}\": {current} -> {PluginDirectory}");
            changes.Add($"Install the plugin {ClaudeSettingsEditor.PluginId} (user scope), workspace roots: {rootsText}");

            var pluginDirs = ClaudeSettingsEditor.GetPluginDirs(_settings.Root);
            if (pluginDirs != null)
                warnings.Add($"env.{ClaudeSettingsEditor.PluginDirsVariable} is set to \"{pluginDirs}\" in {SettingsPath}: " +
                             "a plugin loaded from there replaces the installed one. Remove it unless you mean it " +
                             "(for example, a development build).");
        }

        if (method == Method.PluginDirs && ClaudeSettingsEditor.IsMarketplacePluginEnabled(_settings.Root))
            warnings.Add(MarketplacePluginReplaced());

        var trial = _settings.Root.DeepClone().AsObject();
        try
        {
            if (ClaudeSettingsEditor.AddAdditionalDirectory(trial, _outputDirectory))
                changes.Add($"{SettingsPath}: allow the folder {_outputDirectory} (permissions.additionalDirectories)");
            if (method == Method.PluginDirs)
                PlanPluginDirs(trial, rootsText, changes);
        }
        catch (InvalidDataException ex)
        {
            output.WriteLine($"{SettingsPath}: {ex.Message}. Nothing was changed.");
            return 1;
        }

        if (!Directory.Exists(_outputDirectory))
            changes.Add($"Create the folder {_outputDirectory}");

        GlobalRules? rules = null;
        if (globalRules)
        {
            if (!GlobalRules.TryPrepare(_installDirectory, RulesPath, roots, out rules, out var rulesError))
            {
                output.WriteLine(rulesError + " Nothing was changed.");
                return 1;
            }
            changes.Add(rules!.Describe());
        }

        if (_settings.HadComments)
            warnings.Add($"{SettingsPath} has comments: they are not kept when init writes the file " +
                         "(the original is kept in the Settings folder).");

        output.WriteLine();
        output.WriteLine("Changes:");
        foreach (var change in changes)
            output.WriteLine("  - " + change);
        foreach (var warning in warnings)
            output.WriteLine("  WARNING: " + warning);
        output.WriteLine();

        if (!_prompt.AskYesNo("Apply?"))
        {
            output.WriteLine("Nothing changed.");
            return 0;
        }

        // ---- apply ----
        SettingsBackup? backup = null;
        var problems = 0;
        var marketplaceLeftover = false;
        string? rulesManualCopy = null;
        try
        {
            backup = SettingsBackup.Start(_installDirectory, HarnessFolderName, DateTime.Now);
            backup.SaveBefore(SettingsPath);
            Directory.CreateDirectory(_outputDirectory);

            if (method == Method.Marketplace)
            {
                var added = RunClaude("plugin", "marketplace", "add", PluginDirectory);
                var installed = added && RunClaude("plugin", "install", ClaudeSettingsEditor.PluginId,
                    "--scope", "user", "--config", "workspaceRoots=" + rootsText);

                if (installed)
                {
                    if (!RunClaude("plugin", "update", ClaudeSettingsEditor.PluginId))
                    {
                        output.WriteLine("WARNING: the plugin is installed, but its recorded version was not updated; " +
                                         "/plugin may show an older version number.");
                    }
                }
                else
                {
                    marketplaceLeftover = added;
                    var choice = AskAfterMarketplaceFailure();
                    if (choice == null)
                    {
                        backup.SaveAfter(SettingsPath);
                        output.WriteLine("Stopped.");
                        WriteCopies(backup);
                        return 1;
                    }
                    method = choice.Value;
                    problems++;
                }
            }

            // Claude's commands have rewritten settings.json: read it again before writing init's own entries.
            if (!JsonFile.TryParse(SettingsBackup.ReadIfExists(SettingsPath), out var settings, out var parseError))
            {
                output.WriteLine($"{SettingsPath}: {parseError}; init's own entries were not written.");
                backup.SaveAfter(SettingsPath);
                problems++;
            }
            else
            {
                var changed = false;
                try
                {
                    changed |= ClaudeSettingsEditor.AddAdditionalDirectory(settings!.Root, _outputDirectory);
                    if (method == Method.PluginDirs)
                    {
                        changed |= ClaudeSettingsEditor.SetPluginDirs(settings.Root, PluginDirectory);
                        changed |= ClaudeSettingsEditor.SetInlineWorkspaceRoots(settings.Root, rootsText);
                    }
                }
                catch (InvalidDataException ex)
                {
                    output.WriteLine($"{SettingsPath}: {ex.Message}; init's own entries were not written.");
                    changed = false;
                    problems++;
                }

                if (changed)
                    backup.Write(SettingsPath, settings!.Serialize());
                else
                    backup.SaveAfter(SettingsPath);
            }

            if (rules != null)
                rulesManualCopy = rules.Apply(backup);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            output.WriteLine();
            output.WriteLine("ERROR: " + ex.Message);
            if (backup != null)
                WriteCopies(backup);
            return 1;
        }

        // ---- summary ----
        output.WriteLine();
        output.WriteLine(problems == 0 ? "Done." : "Done, with problems (see above).");
        WriteCopies(backup);
        output.WriteLine();
        output.WriteLine("Next:");

        switch (method)
        {
            case Method.Marketplace:
            case Method.PluginDirs:
                output.WriteLine("  - Start a new Claude Code session and check that /plugin shows \"1 mod active - vsagentbridge\".");
                break;
            case Method.Manual:
                output.WriteLine("  - Set up the plugin by hand: README, section \"Alternative without the marketplace\".");
                break;
        }

        if (method == Method.PluginDirs)
            output.WriteLine($"  - The plugin is loaded through {ClaudeSettingsEditor.PluginDirsVariable}; " +
                             "an organization's managed settings can turn this off.");
        if (method == Method.PluginDirs && !noMarketplace && ClaudeSettingsEditor.IsMarketplacePluginEnabled(_settings.Root))
            output.WriteLine("  - " + MarketplacePluginReplaced());
        if (marketplaceLeftover)
            output.WriteLine($"  - The marketplace \"{ClaudeSettingsEditor.MarketplaceName}\" stays in {SettingsPath}; " +
                             $"remove it if you like: claude plugin marketplace remove {ClaudeSettingsEditor.MarketplaceName}");
        if (rulesManualCopy != null)
            output.WriteLine($"  - Copy the rules from {rulesManualCopy} into {RulesPath} by hand.");
        if (!globalRules)
            output.WriteLine($"  - Rules: copy {Path.Combine(_installDirectory, "AGENTS.sample.md")} as CLAUDE.md into each " +
                             "folder you start Claude Code in (if the file exists, append it).");

        return problems == 0 ? 0 : 1;
    }

    private static string MarketplacePluginReplaced() =>
        $"The plugin {ClaudeSettingsEditor.PluginId} is installed from the marketplace; the plugin loaded through " +
        $"{ClaudeSettingsEditor.PluginDirsVariable} replaces it. If you no longer need it: " +
        $"claude plugin uninstall {ClaudeSettingsEditor.PluginId}";

    private void PlanPluginDirs(JsonObject trial, string rootsText, List<string> changes)
    {
        if (ClaudeSettingsEditor.SetPluginDirs(trial, PluginDirectory))
            changes.Add($"{SettingsPath}: env.{ClaudeSettingsEditor.PluginDirsVariable} = {PluginDirectory}");
        if (ClaudeSettingsEditor.SetInlineWorkspaceRoots(trial, rootsText))
            changes.Add($"{SettingsPath}: workspace roots of {ClaudeSettingsEditor.InlinePluginId}: {rootsText}");
    }

    // Writes the command and its output; true if it exited with 0.
    private bool RunClaude(params string[] arguments)
    {
        var output = _prompt.Output;
        output.WriteLine();
        output.WriteLine("> claude " + string.Join(" ", arguments.Select(ExternalCommand.QuoteArgument)));

        var result = _claude.Run(arguments);
        foreach (var line in (result.Output + result.Error).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            output.WriteLine("  " + line);

        if (result.TimedOut)
            output.WriteLine($"  (no answer within {ExternalCommand.DefaultTimeout.TotalMinutes:0} minutes: stopped)");
        else if (result.ExitCode != 0)
            output.WriteLine($"  (exit code {result.ExitCode})");

        return !result.TimedOut && result.ExitCode == 0;
    }

    private Method? AskAfterMarketplaceFailure()
    {
        var output = _prompt.Output;
        output.WriteLine();
        output.WriteLine("WARNING: the marketplace could not be used (it may be restricted by your organization).");
        var choice = _prompt.AskChoice(new[]
        {
            $"Use the alternative way: {ClaudeSettingsEditor.PluginDirsVariable}, plus all other settings",
            "Apply all other settings only; set up the plugin manually (README, \"Alternative without the marketplace\")",
            "Stop without further changes",
        });

        return choice switch
        {
            1 => Method.PluginDirs,
            2 => Method.Manual,
            _ => null,
        };
    }

    private void WriteCopies(SettingsBackup backup)
    {
        _prompt.Output.WriteLine($"Copies of the changed files (before and after): {backup.RunFolder}");
        if (backup.IsFirstRun)
            _prompt.Output.WriteLine($"Originals from before the first setup: {backup.InitFolder}");
    }
}