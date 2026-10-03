namespace VSAgentBridge.Logic;

/// <summary>What the hook does before the agent edits an existing file.</summary>
internal enum EditAction
{
    /// <summary>Let the edit through unchanged.</summary>
    None,

    /// <summary>Check the file out through Visual Studio, then let the edit through.</summary>
    Checkout,

    /// <summary>Send the edit to a copy under Output; the original stays untouched.</summary>
    Redirect,
}