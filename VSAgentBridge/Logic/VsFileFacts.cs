namespace VSAgentBridge.Logic;

/// <summary>
/// What Visual Studio says about a file. Filled in by the Vs layer; plain data, no COM.
/// </summary>
internal sealed class VsFileFacts
{
    public static VsFileFacts NotFound { get; } = new(instanceFound: false, isUnderScc: false, isCheckedOut: false, error: null);

    public static VsFileFacts Failed(string error) => new(instanceFound: false, isUnderScc: false, isCheckedOut: false, error);

    public VsFileFacts(bool instanceFound, bool isUnderScc, bool isCheckedOut, string? error)
    {
        InstanceFound = instanceFound;
        IsUnderScc = isUnderScc;
        IsCheckedOut = isCheckedOut;
        Error = error;
    }

    /// <summary>An open solution in some Visual Studio instance contains the file.</summary>
    public bool InstanceFound { get; }

    /// <summary>SourceControl.IsItemUnderSCC from the selected instance.</summary>
    public bool IsUnderScc { get; }

    /// <summary>SourceControl.IsItemCheckedOut from the selected instance.</summary>
    public bool IsCheckedOut { get; }

    /// <summary>Set when Visual Studio could not be asked; the other values are then meaningless.</summary>
    public string? Error { get; }
}