using VSAgentBridge.Logic;

namespace VSAgentBridge.Init;

/// <summary>
/// The global rules step of init, the same for both agents: the rules block from AGENTS.global.sample.md
/// with the workspace roots, put into the agent's global rules file (CLAUDE.md or AGENTS.md).
/// Prepared first without changing anything, applied after the user confirms.
/// </summary>
internal sealed class GlobalRules
{
    public const string ManualCopyFileName = "vsab-rules.md";

    private readonly string _block;
    private readonly string? _newText;
    private readonly bool _fileExists;

    private GlobalRules(string rulesPath, string block, RulesBlockChange change, string? newText, bool fileExists)
    {
        RulesPath = rulesPath;
        _block = block;
        Change = change;
        _newText = newText;
        _fileExists = fileExists;
    }

    /// <summary>The agent's global rules file.</summary>
    public string RulesPath { get; }

    public RulesBlockChange Change { get; }

    /// <summary>True if <see cref="Apply"/> will write the rules file.</summary>
    public bool WillWrite => Change == RulesBlockChange.Appended || Change == RulesBlockChange.Replaced;

    /// <summary>
    /// Builds the block for <paramref name="roots"/> from the sample in <paramref name="installDirectory"/> and
    /// works out what happens to <paramref name="rulesPath"/>. Changes nothing. Returns false with a message if
    /// the sample is missing or broken. I/O errors reading the rules file are left to the caller.
    /// </summary>
    public static bool TryPrepare(
        string installDirectory, string rulesPath, IReadOnlyList<string> roots, out GlobalRules? rules, out string? error)
    {
        rules = null;
        var samplePath = Path.Combine(installDirectory, RulesBlock.SampleFileName);
        if (!File.Exists(samplePath))
        {
            error = $"{samplePath} is missing: extract the whole package again.";
            return false;
        }

        if (!RulesBlock.TryRender(File.ReadAllText(samplePath), roots, out var block, out error))
            return false;

        var text = SettingsBackup.ReadIfExists(rulesPath);
        var change = RulesBlock.Merge(text, block!, out var newText);
        rules = new GlobalRules(rulesPath, block!, change, newText, fileExists: text != null);
        return true;
    }

    /// <summary>One line for the list of changes shown before "Apply?".</summary>
    public string Describe() => Change switch
    {
        RulesBlockChange.Appended when !_fileExists => $"{RulesPath}: create it with the bridge's rules",
        RulesBlockChange.Appended => $"{RulesPath}: add the bridge's rules at the end",
        RulesBlockChange.Replaced => $"{RulesPath}: update the bridge's rules (between {RulesBlock.BeginMarker} and {RulesBlock.EndMarker})",
        RulesBlockChange.Unchanged => $"{RulesPath}: the bridge's rules are up to date",
        _ => $"{RulesPath}: WARNING: the markers {RulesBlock.BeginMarker} and {RulesBlock.EndMarker} are not one pair; " +
             "the file is left as it is, the new rules are saved for you to copy by hand",
    };

    /// <summary>
    /// Writes the rules file through <paramref name="backup"/> (copy before, then the new text), or nothing if the
    /// block is up to date. When the markers are ambiguous, saves the block to the run's folder instead and
    /// returns that file's path; otherwise returns null.
    /// </summary>
    public string? Apply(SettingsBackup backup)
    {
        if (WillWrite)
        {
            backup.SaveBefore(RulesPath);
            backup.Write(RulesPath, _newText!);
            return null;
        }

        if (Change != RulesBlockChange.Ambiguous)
            return null;

        var manualCopy = Path.Combine(backup.RunFolder, ManualCopyFileName);
        File.WriteAllText(manualCopy, _block.Replace("\n", "\r\n") + "\r\n");
        return manualCopy;
    }
}