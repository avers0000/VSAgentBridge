namespace VSAgentBridge.Logic;

/// <summary>One command inside a shell command line that changes files: what it does, how it was written, which paths it names.</summary>
internal sealed class ShellFinding
{
    public ShellFinding(ShellOperation operation, string command, IReadOnlyList<string> paths)
    {
        Operation = operation;
        Command = command;
        Paths = paths;
    }

    public ShellOperation Operation { get; }

    /// <summary>
    /// A delete or a move/rename: in v1 refused and recorded, the model lists it for the user.
    /// The other operations write files: the model is sent to the write/edit tools.
    /// </summary>
    public bool IsDeleteOrMove => Operation == ShellOperation.Delete || Operation == ShellOperation.Move;

    /// <summary>The command as written ("Remove-Item", "git mv", "&gt;", "[IO.File]::Delete"); for texts and logs.</summary>
    public string Command { get; }

    /// <summary>
    /// Paths the command names, as written (relative paths unresolved). Only for the text to the model and the record
    /// of the request: the decision never depends on them. May be empty (a variable, pipeline input).
    /// </summary>
    public IReadOnlyList<string> Paths { get; }

    public override string ToString() => $"{Operation} {Command} [{string.Join(", ", Paths)}]";
}