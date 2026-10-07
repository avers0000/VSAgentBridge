using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class HookRequestShellTests
{
    [TestMethod]
    public void Parse_Shell_CommandAndWorkdir_NoPath()
    {
        var json = """{"event":"shell","tool":"shell","command":"Remove-Item x.cs","workdir":"Sub","cwd":"D:\\Work","harness":"opencode","session":"ses_1"}""";

        var request = HookRequest.Parse(json, out var error);

        Assert.IsNotNull(request, error);
        Assert.IsTrue(request.IsShell);
        Assert.IsFalse(request.IsRead || request.IsAfter || request.IsDelete || request.IsRename);
        Assert.AreEqual("Remove-Item x.cs", request.Command);
        Assert.AreEqual("Sub", request.Workdir);
        Assert.AreEqual("", request.FilePath);
        Assert.AreEqual("ses_1", request.Session);
        Assert.AreEqual("", error);
    }

    [TestMethod]
    public void Parse_Shell_PathIsIgnored_WorkdirOptional()
    {
        var request = HookRequest.Parse("""{"event":"shell","tool":"Bash","command":"ls","path":"x.cs"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.AreEqual("", request.FilePath);
        Assert.IsNull(request.Workdir);
    }

    [TestMethod]
    public void Parse_Shell_MultiLineCyrillicCommand()
    {
        var request = HookRequest.Parse("{\"event\":\"shell\",\"tool\":\"shell\",\"command\":\"cd Папка\\r\\nrm x\"}", out var error);

        Assert.IsNotNull(request, error);
        Assert.AreEqual("cd Папка\r\nrm x", request.Command);
    }

    [TestMethod]
    public void Parse_Delete()
    {
        var request = HookRequest.Parse("""{"event":"delete","tool":"patch","path":"a.cs","cwd":"D:\\Work"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsTrue(request.IsDelete);
        Assert.AreEqual("a.cs", request.FilePath);
        Assert.IsNull(request.NewPath);
        Assert.IsNull(request.Command);
    }

    [TestMethod]
    public void Parse_Rename()
    {
        var request = HookRequest.Parse("""{"event":"rename","tool":"patch","path":"a.cs","newPath":"Sub\\b.cs"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsTrue(request.IsRename);
        Assert.AreEqual("a.cs", request.FilePath);
        Assert.AreEqual(@"Sub\b.cs", request.NewPath);
    }

    [TestMethod]
    public void Parse_OtherEvents_IgnoreCommandAndNewPath()
    {
        var request = HookRequest.Parse("""{"event":"before","tool":"edit","path":"a.cs","command":"rm x","newPath":"b.cs","workdir":"w"}""", out var error);

        Assert.IsNotNull(request, error);
        Assert.IsNull(request.Command);
        Assert.IsNull(request.NewPath);
        Assert.IsNull(request.Workdir);
    }

    [TestMethod]
    [DataRow("""{"event":"shell","tool":"shell"}""", "'command'", DisplayName = "shell without command")]
    [DataRow("""{"event":"shell","tool":"shell","command":"  "}""", "'command'", DisplayName = "shell with blank command")]
    [DataRow("""{"event":"shell","tool":"shell","command":1}""", "'command'", DisplayName = "shell command not a string")]
    [DataRow("""{"event":"shell","command":"ls"}""", "'tool'", DisplayName = "shell without tool")]
    [DataRow("""{"event":"shell","tool":"shell","command":"ls","cwd":"rel"}""", "'cwd'", DisplayName = "shell with relative cwd")]
    [DataRow("""{"event":"delete","tool":"patch"}""", "'path'", DisplayName = "delete without path")]
    [DataRow("""{"event":"rename","tool":"patch","path":"a.cs"}""", "'newPath'", DisplayName = "rename without newPath")]
    [DataRow("""{"event":"rename","tool":"patch","newPath":"b.cs"}""", "'path'", DisplayName = "rename without path")]
    public void Parse_Invalid(string json, string expectedInError)
    {
        Assert.IsNull(HookRequest.Parse(json, out var error));
        Assert.IsTrue(error.Contains(expectedInError), $"Error was: {error}");
    }
}
