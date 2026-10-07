namespace VSAgentBridge.Logic;

/// <summary>
/// What a shell command found by <see cref="ShellCommandScanner"/> does to files (slice 4 results, §1.1).
/// v1 only tells writes (the model is sent to the write/edit tools) from deletes and moves (the model lists them
/// for the user); the finer kinds are for v2, where each is done the Visual Studio way: a write as a checkout or
/// an added file, a copy as a write of its destination, a delete or a move through the project system.
/// </summary>
internal enum ShellOperation
{
    /// <summary>Writes into a file: creates, overwrites, appends to or empties it (&gt;, Set-Content, sed -i, ...).</summary>
    Write,

    /// <summary>Copies files: the destination is written, the source only read (cp, Copy-Item, robocopy).</summary>
    Copy,

    /// <summary>Clears the read-only attribute (attrib -R, chmod +w): the edit tool could then write past a TFVC checkout.</summary>
    ClearReadOnly,

    /// <summary>Deletes files or folders (rm, del, Remove-Item, git rm, tf delete, robocopy /MIR).</summary>
    Delete,

    /// <summary>Moves or renames files or folders: a rename is a move within one folder (mv, ren, Move-Item, git mv).</summary>
    Move,
}