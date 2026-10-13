namespace VSAgentBridge.Logic;

/// <summary>What happens to the agent's rules file (CLAUDE.md, AGENTS.md) when init writes the bridge's rules block.</summary>
internal enum RulesBlockChange
{
    /// <summary>The file has no block (or does not exist yet): the block is added at the end.</summary>
    Appended,

    /// <summary>The file has one block, different from the new one: its content is replaced.</summary>
    Replaced,

    /// <summary>The file already has exactly this block: nothing to write.</summary>
    Unchanged,

    /// <summary>
    /// The markers are not one begin/end pair (one is missing, several pairs, end before begin):
    /// the file is left untouched and the user decides.
    /// </summary>
    Ambiguous,
}