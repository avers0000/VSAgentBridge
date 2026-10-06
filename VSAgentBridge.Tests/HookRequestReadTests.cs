using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class HookRequestReadTests
{
    [TestMethod]
    public void Parse_ReadEvent_ReadsAllFields()
    {
        var json = """
            {"event":"read","tool":"read","path":"src\\a.cs","cwd":"D:\\work","harness":"opencode","session":"s1"}
            """;

        var request = HookRequest.Parse(json, out var error);

        Assert.IsNotNull(request, error);
        Assert.AreEqual("read", request.Event);
        Assert.IsTrue(request.IsRead);
        Assert.IsFalse(request.IsAfter);
        Assert.AreEqual(@"src\a.cs", request.FilePath);
        Assert.AreEqual("s1", request.Session);
        Assert.AreEqual("", error);
    }

    [TestMethod]
    public void Parse_BeforeEvent_IsNeitherReadNorAfter()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"edit","path":"C:\\x.txt"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsFalse(request.IsRead);
        Assert.IsFalse(request.IsAfter);
    }

    [TestMethod]
    public void Parse_UnsupportedEvent_ListsAllThree()
    {
        Assert.IsNull(HookRequest.Parse("""{"event":"delete","tool":"edit","path":"C:\\x.txt"}""", out var error));
        Assert.IsTrue(error.Contains("'before', 'after' or 'read'"), error);
    }
}
