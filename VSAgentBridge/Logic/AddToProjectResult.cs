namespace VSAgentBridge.Logic;

/// <summary>
/// Outcome of adding a new file to a project through Visual Studio, with the facts checked after the call.
/// Filled in by the Vs layer; plain data, no COM.
/// </summary>
internal sealed class AddToProjectResult
{
    public AddToProjectResult(
        string filePath,
        int foldersAdded,
        int attempts,
        string? addedItemPath,
        bool itemFound,
        bool projectSaved,
        bool? isUnderScc,
        bool? isCheckedOut,
        string? error)
    {
        FilePath = filePath;
        FoldersAdded = foldersAdded;
        Attempts = attempts;
        AddedItemPath = addedItemPath;
        ItemFound = itemFound;
        ProjectSaved = projectSaved;
        IsUnderScc = isUnderScc;
        IsCheckedOut = isCheckedOut;
        Error = error;
    }

    /// <summary>The new file that was to be added.</summary>
    public string FilePath { get; }

    /// <summary>Number of folders added with AddFolder (levels of the chain missing in the project).</summary>
    public int FoldersAdded { get; }

    /// <summary>Number of AddFromFile calls; more than one when Visual Studio answered E_PENDING.</summary>
    public int Attempts { get; }

    /// <summary>
    /// Path of the item that AddFromFile returned; null if it returned nothing. Differs from
    /// <see cref="FilePath"/> when the project system copied the file elsewhere instead of adding it in place.
    /// </summary>
    public string? AddedItemPath { get; }

    /// <summary>After the call, the solution finds an item with <see cref="FilePath"/> (Solution.FindProjectItem).</summary>
    public bool ItemFound { get; }

    /// <summary>Project.Saved after Project.Save(): the project file on disk contains the new item.</summary>
    public bool ProjectSaved { get; }

    /// <summary>SourceControl.IsItemUnderSCC after adding; logged only (its meaning for a pending add is not known yet).</summary>
    public bool? IsUnderScc { get; }

    /// <summary>SourceControl.IsItemCheckedOut after adding; logged only.</summary>
    public bool? IsCheckedOut { get; }

    /// <summary>Set when a call to Visual Studio failed.</summary>
    public string? Error { get; }

    /// <summary>The project system added a copy in another place instead of the file itself.</summary>
    public bool IsCopy =>
        AddedItemPath != null
        && !string.Equals(Path.GetFullPath(AddedItemPath), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Judged by the state after the call, not by the absence of an exception (spec §4.6a).</summary>
    public bool Succeeded => Error == null && ItemFound && !IsCopy && ProjectSaved;
}