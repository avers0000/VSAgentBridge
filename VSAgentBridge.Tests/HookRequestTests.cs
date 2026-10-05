using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class HookRequestTests
{
    [TestMethod]
    public void Parse_FullRequest_ReadsAllFields()
    {
        var json = """
            {"event":"before","tool":"edit","path":"src\\a.cs","cwd":"D:\\work","harness":"opencode","session":"s1"}
            """;

        var request = HookRequest.Parse(json, out var error);

        Assert.IsNotNull(request, error);
        Assert.AreEqual("before", request.Event);
        Assert.AreEqual("edit", request.Tool);
        Assert.AreEqual(@"src\a.cs", request.FilePath);
        Assert.AreEqual(@"D:\work", request.Cwd);
        Assert.AreEqual("opencode", request.Harness);
        Assert.AreEqual("s1", request.Session);
        Assert.AreEqual("", error);
    }

    [TestMethod]
    public void Parse_AfterEvent_ReadsAllFields()
    {
        var json = """
            {"event":"after","tool":"write","path":"src\\new.cs","cwd":"D:\\work","harness":"opencode","session":"s1"}
            """;

        var request = HookRequest.Parse(json, out var error);

        Assert.IsNotNull(request, error);
        Assert.AreEqual("after", request.Event);
        Assert.IsTrue(request.IsAfter);
        Assert.AreEqual("write", request.Tool);
        Assert.AreEqual(@"src\new.cs", request.FilePath);
        Assert.AreEqual(@"D:\work", request.Cwd);
        Assert.AreEqual("", error);
    }

    [TestMethod]
    public void Parse_BeforeEvent_IsNotAfter()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"edit","path":"C:\\x.txt"}""", out _);

        Assert.IsNotNull(request);
        Assert.IsFalse(request.IsAfter);
    }

    [TestMethod]
    public void Parse_MinimalRequest_OptionalFieldsAreNull()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"write","path":"C:\\x.txt"}""", out _);

        Assert.IsNotNull(request);
        Assert.IsNull(request.Cwd);
        Assert.IsNull(request.Harness);
        Assert.IsNull(request.Session);
    }

    [TestMethod]
    public void Parse_UnknownFields_AreIgnored()
    {
        var request = HookRequest.Parse(
            """{"event":"before","tool":"edit","path":"C:\\x.txt","input":{"oldString":"a"},"extra":42}""", out _);

        Assert.IsNotNull(request);
    }

    [TestMethod]
    public void Parse_NonAsciiPath_IsKept()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"edit","path":"D:\\Проект\\файл.cs"}""", out _);

        Assert.IsNotNull(request);
        Assert.AreEqual(@"D:\Проект\файл.cs", request.FilePath);
    }

    [TestMethod]
    [DataRow("""{"tool":"edit","path":"C:\\x.txt"}""", "'event'", DisplayName = "event missing")]
    [DataRow("""{"event":1,"tool":"edit","path":"C:\\x.txt"}""", "'event'", DisplayName = "event not a string")]
    [DataRow("""{"event":"delete","tool":"edit","path":"C:\\x.txt"}""", "unsupported event 'delete'", DisplayName = "event unsupported")]
    [DataRow("""{"event":"Before","tool":"edit","path":"C:\\x.txt"}""", "unsupported event", DisplayName = "event is case-sensitive")]
    [DataRow("""{"event":"before","path":"C:\\x.txt"}""", "'tool'", DisplayName = "tool missing")]
    [DataRow("""{"event":"before","tool":"","path":"C:\\x.txt"}""", "'tool'", DisplayName = "tool empty")]
    [DataRow("""{"event":"before","tool":"edit"}""", "'path'", DisplayName = "path missing")]
    [DataRow("""{"event":"before","tool":"edit","path":"   "}""", "'path'", DisplayName = "path blank")]
    [DataRow("""{"event":"before","tool":"edit","path":null}""", "'path'", DisplayName = "path null")]
    [DataRow("""{"event":"before","tool":"edit","path":"a.cs","cwd":"relative\\dir"}""", "'cwd'", DisplayName = "cwd relative")]
    [DataRow("""[1,2]""", "JSON object", DisplayName = "root is an array")]
    [DataRow("""oops""", "invalid JSON", DisplayName = "not JSON")]
    [DataRow("", "invalid JSON", DisplayName = "empty input")]
    public void Parse_InvalidRequest_ReturnsNullWithReason(string json, string expectedInError)
    {
        var request = HookRequest.Parse(json, out var error);

        Assert.IsNull(request);
        Assert.IsTrue(error.Contains(expectedInError), $"Error was: {error}");
    }
}
