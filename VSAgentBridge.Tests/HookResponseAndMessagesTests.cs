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
    [DataRow(EditReason.ReadOnlyNotTfvc)]
    [DataRow(EditReason.ReadOnlyInLocalWorkspace)]
    [DataRow(EditReason.NotInOpenSolution)]
    [DataRow(EditReason.VisualStudioError)]
    [DataRow(EditReason.ExcludedFromSourceControl)]
    [DataRow(EditReason.CheckedOutButReadOnly)]
    public void ForRedirect_EveryRedirectReason_HasSpecificText(EditReason reason)
    {
        var text = HookMessages.ForRedirect(reason);

        AssertCommonShape(text);
        Assert.IsFalse(text.Contains("could not be prepared"), $"Fell back to the generic text for {reason}");
    }

    [TestMethod]
    public void ForRedirect_RedirectReasons_AllTextsDiffer()
    {
        var texts = RedirectReasons.Select(HookMessages.ForRedirect).ToList();

        Assert.AreEqual(texts.Count, texts.Distinct().Count());
    }

    [TestMethod]
    public void ForRedirect_NotInOpenSolution_MentionsElevation()
    {
        Assert.IsTrue(HookMessages.ForRedirect(EditReason.NotInOpenSolution).Contains("elevation level"));
    }

    [TestMethod]
    public void ForRedirect_UnexpectedReason_GenericText()
    {
        var text = HookMessages.ForRedirect(EditReason.NeedsCheckout);

        AssertCommonShape(text);
        Assert.IsTrue(text.Contains("could not be prepared (NeedsCheckout)"), text);
    }

    [TestMethod]
    public void ForCheckoutFailure_Error_IncludesError()
    {
        var result = new CheckoutResult(false, 1, false, false, 0, "COMException 0x80004005: boom");

        var text = HookMessages.ForCheckoutFailure(result);

        AssertCommonShape(text);
        Assert.IsTrue(text.Contains("COMException 0x80004005: boom"), text);
    }

    [TestMethod]
    public void ForCheckoutFailure_StillReadOnly_SaysSo()
    {
        var text = HookMessages.ForCheckoutFailure(new CheckoutResult(true, 1, false, true, 2000, null));

        Assert.IsTrue(text.Contains("still read-only"), text);
    }

    [TestMethod]
    public void ForCheckoutFailure_NotCheckedOut_SaysSo()
    {
        var text = HookMessages.ForCheckoutFailure(new CheckoutResult(true, 1, true, false, 0, null));

        Assert.IsTrue(text.Contains("does not report the file as checked out"), text);
    }

    [TestMethod]
    public void InternalError_HasCommonShape()
    {
        AssertCommonShape(HookMessages.InternalError);
    }

    private static void AssertCommonShape(string text)
    {
        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked this edit: ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("The file was not changed."), text);
        Assert.IsTrue(text.Contains("Do not retry this edit"), text);
        Assert.IsTrue(text.Contains("tell the user: "), text);
    }
}
