namespace VSAgentBridge.Logic;

/// <summary>
/// Outcome of a checkout through Visual Studio, with the facts checked after the call.
/// Filled in by the Vs layer; plain data, no COM.
/// </summary>
internal sealed class CheckoutResult
{
    public CheckoutResult(
        bool checkOutItemReturned,
        int attempts,
        bool isWritable,
        bool isCheckedOut,
        long verifyWaitMs,
        string? error)
    {
        CheckOutItemReturned = checkOutItemReturned;
        Attempts = attempts;
        IsWritable = isWritable;
        IsCheckedOut = isCheckedOut;
        VerifyWaitMs = verifyWaitMs;
        Error = error;
    }

    /// <summary>Value returned by SourceControl.CheckOutItem; logged, not trusted on its own.</summary>
    public bool CheckOutItemReturned { get; }

    /// <summary>Number of CheckOutItem calls; more than one when Visual Studio answered E_PENDING.</summary>
    public int Attempts { get; }

    /// <summary>The file has no read-only attribute after the call.</summary>
    public bool IsWritable { get; }

    /// <summary>SourceControl.IsItemCheckedOut after the call.</summary>
    public bool IsCheckedOut { get; }

    /// <summary>How long verification waited for the read-only attribute to clear.</summary>
    public long VerifyWaitMs { get; }

    /// <summary>Set when a call to Visual Studio or the file system failed.</summary>
    public string? Error { get; }

    /// <summary>Judged by the state after the call, not by the absence of an exception (spec §4.6a).</summary>
    public bool Succeeded => Error == null && IsWritable && IsCheckedOut;
}
