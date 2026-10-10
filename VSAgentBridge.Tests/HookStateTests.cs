using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

/// <summary>"state" handed from the "before" response to the "after" request (slice 6 results, §1.13).</summary>
[TestClass]
internal sealed class HookStateTests
{
    [TestMethod]
    public void Parse_AfterWithState_ReadsIt()
    {
        var request = HookRequest.Parse(
            """{"event":"after","tool":"Write","path":"C:\\x.cs","state":"existing bom crlf final"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsTrue(request.IsAfter);
        Assert.AreEqual("existing bom crlf final", request.State);
    }

    [TestMethod]
    public void Parse_AfterWithoutState_Null()
    {
        var request = HookRequest.Parse("""{"event":"after","tool":"Write","path":"C:\\x.cs"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsNull(request.State);
    }

    [TestMethod]
    public void Parse_AfterWithStateNotAString_NullAndStillValid()
    {
        var request = HookRequest.Parse("""{"event":"after","tool":"Write","path":"C:\\x.cs","state":1}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsNull(request.State);
    }

    [TestMethod]
    public void Parse_BeforeWithState_Ignored()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"Write","path":"C:\\x.cs","state":"new"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsNull(request.State);
    }

    [TestMethod]
    public void ToJson_AllowThenAfterExisting_HasState()
    {
        var response = HookResponse.AllowThenAfter("Writable", WriteState.Existing(bom: true, crlf: true, finalNewline: true));

        Assert.IsTrue(response.CallAfter);
        Assert.AreEqual(
            """{"decision":"allow","reason":"Writable","after":true,"state":"existing bom crlf final"}""",
            response.ToJson());
    }

    [TestMethod]
    public void ToJson_Allow_NoAfterNoState()
    {
        var response = HookResponse.Allow("Writable");

        Assert.IsFalse(response.CallAfter);
        Assert.IsNull(response.State);
        Assert.AreEqual("""{"decision":"allow","reason":"Writable"}""", response.ToJson());
    }
}
