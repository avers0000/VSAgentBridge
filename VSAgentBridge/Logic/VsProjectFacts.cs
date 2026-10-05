namespace VSAgentBridge.Logic;

/// <summary>
/// One project of an open solution, as the Vs layer saw it. Plain data, no COM.
/// Solution folders and the Miscellaneous Files project are not projects here: the Vs layer skips them.
/// </summary>
internal sealed class VsProjectFacts
{
    public VsProjectFacts(string name, string filePath, string directory, bool isLoaded)
    {
        Name = name;
        FilePath = filePath;
        Directory = directory;
        IsLoaded = isLoaded;
    }

    /// <summary>Project name as shown in Solution Explorer; for messages and the log.</summary>
    public string Name { get; }

    /// <summary>
    /// Full path of the project file (.csproj, .sqlproj, ...). Identifies the project across instances:
    /// the same file in two open solutions is the same project.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Folder whose files belong to the project: normally the folder of <see cref="FilePath"/>.
    /// Given explicitly because not every project kind has a project file next to its content.
    /// </summary>
    public string Directory { get; }

    /// <summary>False for a project unloaded in this solution: it has no items and cannot take new ones.</summary>
    public bool IsLoaded { get; }
}