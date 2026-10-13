using VSAgentBridge.Logic;

namespace VSAgentBridge.Init;

/// <summary>
/// "vsagentbridge init opencode" after the common steps: opencode.json in each workspace root, the global rules,
/// "Apply?", the summary. OpenCode has no setup commands: init writes everything itself.
/// </summary>
internal static class OpenCodeInit
{
    public const string HarnessFolderName = "opencode";
    public const string ConfigFileName = "opencode.json";

    /// <summary>OpenCode's global rules file: %USERPROFILE%\.config\opencode\AGENTS.md.</summary>
    public static string RulesPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "AGENTS.md");

    /// <summary>Plans, asks "Apply?", applies, writes the summary. Returns the exit code.</summary>
    public static int Run(ConsolePrompt prompt, string installDirectory, string outputDirectory,
        IReadOnlyList<string> roots, bool globalRules)
    {
        var output = prompt.Output;
        var changes = new List<string>();
        var warnings = new List<string>();
        var toWrite = new List<(string Path, JsonFile File)>();

        // ---- plan: nothing is changed here ----
        foreach (var root in roots)
        {
            var path = Path.Combine(root, ConfigFileName);
            string? text;
            try
            {
                text = SettingsBackup.ReadIfExists(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                output.WriteLine($"{path}: {ex.Message}. Nothing was changed.");
                return 1;
            }

            if (!JsonFile.TryParse(text, out var file, out var parseError))
            {
                output.WriteLine($"{path}: {parseError}. Fix the file and run init again; nothing was changed.");
                return 1;
            }

            bool changed;
            string? replaced;
            try
            {
                changed = OpenCodeConfigEditor.Apply(file!.Root, installDirectory, outputDirectory, out replaced);
            }
            catch (InvalidDataException ex)
            {
                output.WriteLine($"{path}: {ex.Message}. Nothing was changed.");
                return 1;
            }

            if (!changed)
            {
                changes.Add($"{path}: the bridge's entries are up to date");
                continue;
            }

            changes.Add(text == null
                ? $"{path}: create it with the bridge's entries"
                : $"{path}: add the bridge's entries");
            if (replaced != null)
                changes.Add($"{path}: plugin of the earlier install folder {replaced} -> {Path.Combine(installDirectory, HarnessFolderName)}");
            if (file.HadComments)
                warnings.Add($"{path} has comments: they are not kept when init writes the file " +
                             "(the original is kept in the Settings folder).");
            if (File.Exists(Path.Combine(root, "opencode.jsonc")))
                warnings.Add($"{root} also has opencode.jsonc; init writes {ConfigFileName}. " +
                             "Make sure OpenCode uses the entries you expect.");

            toWrite.Add((path, file));
        }

        if (!Directory.Exists(outputDirectory))
            changes.Add($"Create the folder {outputDirectory}");

        GlobalRules? rules = null;
        if (globalRules)
        {
            try
            {
                if (!GlobalRules.TryPrepare(installDirectory, RulesPath(), roots, out rules, out var rulesError))
                {
                    output.WriteLine(rulesError + " Nothing was changed.");
                    return 1;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                output.WriteLine($"{RulesPath()}: {ex.Message}. Nothing was changed.");
                return 1;
            }
            changes.Add(rules!.Describe());
        }

        output.WriteLine();
        output.WriteLine("Changes:");
        foreach (var change in changes)
            output.WriteLine("  - " + change);
        foreach (var warning in warnings)
            output.WriteLine("  WARNING: " + warning);
        output.WriteLine();

        // Nothing to write: no question, no Settings folder, no restart needed.
        // Ambiguous rule markers count as a change: Apply saves the block for copying by hand.
        var rulesChange = rules != null && (rules.WillWrite || rules.Change == RulesBlockChange.Ambiguous);
        if (toWrite.Count == 0 && Directory.Exists(outputDirectory) && !rulesChange)
        {
            output.WriteLine("Everything is up to date; nothing to change.");
            return 0;
        }

        if (!prompt.AskYesNo("Apply?"))
        {
            output.WriteLine("Nothing changed.");
            return 0;
        }

        // ---- apply ----
        SettingsBackup? backup = null;
        string? rulesManualCopy = null;
        try
        {
            backup = SettingsBackup.Start(installDirectory, HarnessFolderName, DateTime.Now);
            Directory.CreateDirectory(outputDirectory);
            foreach (var (path, file) in toWrite)
            {
                backup.SaveBefore(path);
                backup.Write(path, file.Serialize());
            }

            if (rules != null)
                rulesManualCopy = rules.Apply(backup);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            output.WriteLine();
            output.WriteLine("ERROR: " + ex.Message);
            if (backup != null)
                WriteCopies(output, backup);
            return 1;
        }

        // ---- summary ----
        output.WriteLine();
        output.WriteLine("Done.");
        WriteCopies(output, backup);
        output.WriteLine();
        output.WriteLine("Next:");
        output.WriteLine("  - Restart the OpenCode service from a terminal with the same elevation as Visual Studio:");
        output.WriteLine("      opencode service restart");
        output.WriteLine($"  - {ConfigFileName} is in your workspace folders: keep it out of version control if it does not belong there.");
        output.WriteLine($"  - {ConfigFileName} files in folders that are not in this list are left as they are.");
        if (rulesManualCopy != null)
            output.WriteLine($"  - Copy the rules from {rulesManualCopy} into {RulesPath()} by hand.");
        if (!globalRules)
            output.WriteLine($"  - Rules: copy {Path.Combine(installDirectory, "AGENTS.sample.md")} as AGENTS.md into each " +
                             "folder you start OpenCode in (if the file exists, append it).");
        return 0;
    }

    private static void WriteCopies(TextWriter output, SettingsBackup backup)
    {
        output.WriteLine($"Copies of the changed files (before and after): {backup.RunFolder}");
        if (backup.IsFirstRun)
            output.WriteLine($"Originals from before the first setup: {backup.InitFolder}");
    }
}