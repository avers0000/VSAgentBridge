using System.Text.Json;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class HookResponseAndMessagesTests
{
    private static readonly EditReason[] RedirectReasons =
    {
        EditReason.ReadOnlyNotTfvc, EditReason.ReadOnlyInLocalWorkspace, EditReason.NotInOpenSolution,
        EditReason.VisualStudioError, EditReason.ExcludedFromSourceControl, EditReason.CheckedOutButReadOnly,
    };

    // ---- HookResponse JSON ----

    [TestMethod]
    public void ToJson_Allow_HasDecisionAndReasonOnly()
    {
        var json = HookResponse.Allow("Writable").ToJson();

        Assert.AreEqual("""{"decision":"allow","reason":"Writable"}""", json);
    }

    [TestMethod]
    public void ToJson_Deny_HasMessage()
    {
        var json = HookResponse.Deny("NotInOpenSolution", "text for the model").ToJson();

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("deny", doc.RootElement.GetProperty("decision").GetString());
        Assert.AreEqual("NotInOpenSolution", doc.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual("text for the model", doc.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    public void ToJson_NonAsciiMessage_OutputIsAsciiAndRoundTrips()
    {
        var message = @"cannot edit D:\Проект\файл.cs ""quoted""";

        var json = HookResponse.Deny("X", message).ToJson();

        Assert.IsTrue(json.All(c => c < 128), $"Output is not ASCII: {json}");
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(message, doc.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    public void ToJson_Redirect_HasPathAndMessage()
    {
        var json = HookResponse.Redirect("NotInOpenSolution", @"D:\Out\s1\a.cs", "saved").ToJson();

        Assert.AreEqual(
            """{"decision":"redirect","reason":"NotInOpenSolution","path":"D:\\Out\\s1\\a.cs","message":"saved"}""", json);
    }

    [TestMethod]
    public void ToJson_RedirectNonAsciiPath_OutputIsAsciiAndRoundTrips()
    {
        var path = @"D:\Выход\ses_1\Проект\файл.cs";

        var json = HookResponse.Redirect("X", path, "m").ToJson();

        Assert.IsTrue(json.All(c => c < 128), $"Output is not ASCII: {json}");
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(path, doc.RootElement.GetProperty("path").GetString());
    }

    [TestMethod]
    public void Redirect_IsNotAllowedAndNeverAsksForAfter()
    {
        var response = HookResponse.Redirect("X", @"D:\Out\a.cs", "m");

        Assert.AreEqual(HookDecision.Redirect, response.Decision);
        Assert.AreEqual("redirect", response.DecisionName);
        Assert.IsFalse(response.Allowed);
        Assert.IsFalse(response.CallAfter);
        Assert.AreEqual(@"D:\Out\a.cs", response.RedirectPath);
    }

    [TestMethod]
    public void DecisionName_AllowAndDeny()
    {
        Assert.AreEqual("allow", HookResponse.Allow("X").DecisionName);
        Assert.AreEqual("deny", HookResponse.Deny("X", "m").DecisionName);
        Assert.IsNull(HookResponse.Allow("X").RedirectPath);
    }

    // ---- HookMessages: causes for an existing file ----

    [TestMethod]
    [DataRow(EditReason.ReadOnlyNotTfvc)]
    [DataRow(EditReason.ReadOnlyInLocalWorkspace)]
    [DataRow(EditReason.NotInOpenSolution)]
    [DataRow(EditReason.VisualStudioError)]
    [DataRow(EditReason.ExcludedFromSourceControl)]
    [DataRow(EditReason.CheckedOutButReadOnly)]
    public void Explain_EveryRedirectReason_HasSpecificCauseAndFix(EditReason reason)
    {
        var (cause, fix) = HookMessages.Explain(reason);

        Assert.IsFalse(cause.Contains("could not be prepared"), $"Fell back to the generic text for {reason}");
        Assert.IsFalse(string.IsNullOrEmpty(fix), $"No fix for {reason}");
        Assert.IsFalse(fix!.Contains("repeat"), $"A redirect needs no repeat: {fix}");
    }

    // Slice 3 results, §4.1: a fix is what the user does before merging, placed as "<fix>, then merge …".
    [TestMethod]
    [DataRow(EditReason.ReadOnlyNotTfvc)]
    [DataRow(EditReason.ReadOnlyInLocalWorkspace)]
    [DataRow(EditReason.NotInOpenSolution)]
    [DataRow(EditReason.VisualStudioError)]
    [DataRow(EditReason.ExcludedFromSourceControl)]
    [DataRow(EditReason.CheckedOutButReadOnly)]
    public void Explain_Fix_IsAnImperativeFragment(EditReason reason)
    {
        AssertFixFragment(HookMessages.Explain(reason).Fix!);
    }

    [TestMethod]
    public void ExplainCheckoutFailure_Fix_IsAnImperativeFragment()
    {
        AssertFixFragment(HookMessages.ExplainCheckoutFailure(new CheckoutResult(true, 1, true, false, 0, null)).Fix!);
    }

    [TestMethod]
    public void Explain_RedirectReasons_AllCausesDiffer()
    {
        var causes = RedirectReasons.Select(r => HookMessages.Explain(r).Cause).ToList();

        Assert.AreEqual(causes.Count, causes.Distinct().Count());
    }

    [TestMethod]
    public void Explain_NotInOpenSolution_FixMentionsElevation()
    {
        Assert.IsTrue(HookMessages.Explain(EditReason.NotInOpenSolution).Fix!.Contains("elevation level"));
    }

    [TestMethod]
    public void Explain_UnexpectedReason_GenericCauseNoFix()
    {
        var (cause, fix) = HookMessages.Explain(EditReason.NeedsCheckout);

        Assert.IsTrue(cause.Contains("could not be prepared (NeedsCheckout)"), cause);
        Assert.IsNull(fix);
    }

    [TestMethod]
    public void ExplainCheckoutFailure_Error_IncludesError()
    {
        var result = new CheckoutResult(false, 1, false, false, 0, "COMException 0x80004005: boom");

        var (cause, fix) = HookMessages.ExplainCheckoutFailure(result);

        Assert.IsTrue(cause.Contains("COMException 0x80004005: boom"), cause);
        Assert.IsFalse(string.IsNullOrEmpty(fix));
    }

    [TestMethod]
    public void ExplainCheckoutFailure_StillReadOnly_SaysSo()
    {
        var (cause, _) = HookMessages.ExplainCheckoutFailure(new CheckoutResult(true, 1, false, true, 2000, null));

        Assert.IsTrue(cause.Contains("still read-only"), cause);
    }

    [TestMethod]
    public void ExplainCheckoutFailure_NotCheckedOut_SaysSo()
    {
        var (cause, _) = HookMessages.ExplainCheckoutFailure(new CheckoutResult(true, 1, true, false, 0, null));

        Assert.IsTrue(cause.Contains("does not report it as checked out"), cause);
    }

    // ---- HookMessages: the remaining denials ----

    [TestMethod]
    public void InternalError_HasDenialShapeForAnEdit()
    {
        var text = HookMessages.InternalError;

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked this edit: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The file was not changed."), text);
        Assert.IsTrue(text.Contains("with any tool"), text);
        Assert.IsTrue(text.Contains("as a diff"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
    }

    [TestMethod]
    public void InternalErrorNewFile_HasDenialShapeForANewFile()
    {
        var text = HookMessages.InternalErrorNewFile;

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked creating this file: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("with any tool"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
        Assert.IsFalse(text.Contains("was not changed"), text);
        Assert.IsFalse(text.Contains("as a diff"), text);
    }

    internal static void AssertFixFragment(string fix)
    {
        Assert.IsFalse(fix.EndsWith(".", StringComparison.Ordinal), fix);
        Assert.IsTrue(char.IsLower(fix[0]), fix);
        Assert.IsTrue(fix.IndexOf("next time", StringComparison.OrdinalIgnoreCase) < 0, fix);
    }
}
