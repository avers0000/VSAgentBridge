namespace VSAgentBridge.Logic;

/// <summary>The verdict of a hook response: what the adapter does with the tool call.</summary>
internal enum HookDecision
{
    /// <summary>Let the tool run as requested.</summary>
    Allow,

    /// <summary>Block the tool; the adapter shows the message to the model as the tool's error.</summary>
    Deny,

    /// <summary>
    /// Let the tool run on another path (the session folder under Output); the adapter replaces the path
    /// in the tool's input and appends the message to the tool's result once it has completed.
    /// </summary>
    Redirect,
}