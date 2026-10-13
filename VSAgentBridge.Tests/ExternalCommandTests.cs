using System.Reflection;
using System.Text;
using VSAgentBridge.Init;

namespace VSAgentBridge.Tests;

// ExternalCommand: finding claude/opencode in PATH, quoting arguments, running a program or a .cmd script.
[TestClass]
internal sealed class ExternalCommandTests
{
    // ---- FindInPath ----

    // PATHEXT is upper case (".EXE"); the path found has the extension in lower case, as files are usually named.
    private static void AssertPath(string expected, string? actual) =>
        Assert.AreEqual(expected, actual);

    private static Func<string, bool> Existing(params string[] files) =>
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Find_Exe()
    {
        AssertPath(@"C:\A\claude.exe",
            ExternalCommand.FindInPath("claude", @"C:\X;C:\A", ".COM;.EXE;.BAT;.CMD", Existing(@"C:\A\claude.exe")));
    }

    [TestMethod]
    public void Find_Cmd()
    {
        AssertPath(@"C:\npm\claude.cmd",
            ExternalCommand.FindInPath("claude", @"C:\npm", ".COM;.EXE;.BAT;.CMD", Existing(@"C:\npm\claude.cmd")));
    }

    [TestMethod]
    public void Find_FirstFolderWins()
    {
        AssertPath(@"C:\A\claude.cmd",
            ExternalCommand.FindInPath("claude", @"C:\A;C:\B", ".EXE;.CMD", Existing(@"C:\B\claude.exe", @"C:\A\claude.cmd")));
    }

    [TestMethod]
    public void Find_InOneFolder_PathExtOrderWins()
    {
        AssertPath(@"C:\A\claude.exe",
            ExternalCommand.FindInPath("claude", @"C:\A", ".EXE;.CMD", Existing(@"C:\A\claude.cmd", @"C:\A\claude.exe")));
        AssertPath(@"C:\A\claude.cmd",
            ExternalCommand.FindInPath("claude", @"C:\A", ".CMD;.EXE", Existing(@"C:\A\claude.cmd", @"C:\A\claude.exe")));
    }

    [TestMethod]
    public void Find_ExtensionsWeCannotRun_Skipped()
    {
        Assert.IsNull(ExternalCommand.FindInPath("claude", @"C:\A", ".JS;.VBS;.PS1",
            Existing(@"C:\A\claude.js", @"C:\A\claude.vbs", @"C:\A\claude.ps1")));
        AssertPath(@"C:\B\claude.exe", ExternalCommand.FindInPath("claude", @"C:\A;C:\B", ".JS;.EXE",
            Existing(@"C:\A\claude.js", @"C:\B\claude.exe")));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void Find_NoPathExt_WindowsDefault(string? pathExt)
    {
        AssertPath(@"C:\A\claude.bat",
            ExternalCommand.FindInPath("claude", @"C:\A", pathExt, Existing(@"C:\A\claude.bat")));
    }

    [TestMethod]
    public void Find_QuotedEmptyAndBadFolders_Handled()
    {
        AssertPath(@"C:\Program Files\X\claude.exe",
            ExternalCommand.FindInPath("claude", @";; C:\Bad|Folder ;""C:\Program Files\X"";", ".EXE",
                Existing(@"C:\Program Files\X\claude.exe")));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Find_NoPath_Null(string? path)
    {
        Assert.IsNull(ExternalCommand.FindInPath("claude", path, ".EXE", _ => true));
    }

    [TestMethod]
    public void Find_NotFound_Null()
    {
        Assert.IsNull(ExternalCommand.FindInPath("claude", @"C:\A;C:\B", ".EXE;.CMD", Existing(@"C:\A\opencode.exe")));
    }

    // ---- QuoteArgument (CommandLineToArgvW rules) ----

    [TestMethod]
    [DataRow("plugin", "plugin")]
    [DataRow("vsagentbridge@vsab", "vsagentbridge@vsab")]
    [DataRow(@"workspaceRoots=D:\A;D:\B", @"workspaceRoots=D:\A;D:\B")]
    [DataRow(@"D:\Dir\", @"D:\Dir\")]
    [DataRow("", "\"\"")]
    [DataRow("a b", "\"a b\"")]
    [DataRow("a\tb", "\"a\tb\"")]
    [DataRow(@"workspaceRoots=D:\My Projects;E:\Work", "\"workspaceRoots=D:\\My Projects;E:\\Work\"")]
    [DataRow(@"D:\My Dir\", "\"D:\\My Dir\\\\\"")]
    [DataRow(@"D:\My Dir\\", "\"D:\\My Dir\\\\\\\\\"")]
    [DataRow("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [DataRow("a\\\"b", "\"a\\\\\\\"b\"")]
    [DataRow(@"a\b c", "\"a\\b c\"")]
    public void Quote(string argument, string expected)
    {
        Assert.AreEqual(expected, ExternalCommand.QuoteArgument(argument));
    }

    // ---- Run: a real program (cmd.exe itself) ----

    [TestMethod]
    public void Run_Exe_OutputAndExitCode()
    {
        var cmd = ExternalCommand.Find("cmd");
        Assert.IsNotNull(cmd, "cmd.exe is expected in PATH");
        StringAssert.EndsWith(cmd!.Path, "cmd.exe", StringComparison.OrdinalIgnoreCase);

        var echo = cmd.Run(new[] { "/d", "/c", "echo hello" });
        Assert.AreEqual(0, echo.ExitCode);
        Assert.AreEqual("hello", echo.Output.Trim());
        Assert.IsFalse(echo.TimedOut);

        Assert.AreEqual(5, cmd.Run(new[] { "/d", "/c", "exit 5" }).ExitCode);
    }

    [TestMethod]
    public void Find_NotInstalled_Null()
    {
        Assert.IsNull(ExternalCommand.Find("vsab-no-such-command-" + Guid.NewGuid().ToString("N")));
    }

    // ---- Run: a .cmd script (the npm way of installing a CLI) ----

    private string _root = "";

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"), "My Scripts");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }

    // The constructor is private (Find is the only way in the product); the tests need a script outside PATH.
    private static ExternalCommand ForPath(string path) =>
        (ExternalCommand)typeof(ExternalCommand)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null)!
            .Invoke(new object[] { Path.GetFileNameWithoutExtension(path), path });

    private string Script(string name, params string[] lines)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", Encoding.ASCII);
        return path;
    }

    [TestMethod]
    public void Run_CmdScript_InFolderWithSpaces_ArgumentsStdinExitCode()
    {
        // "set /p" reads standard input: it would wait forever if input were not closed.
        var script = Script("args.cmd",
            "@echo off",
            "set /p answer=",
            "echo [%~1]",
            "echo [%~2]",
            "echo [%~3]",
            "echo problem 1>&2",
            "exit /b 7");

        var result = ForPath(script).Run(
            new[] { "plugin", @"workspaceRoots=D:\My Projects;E:\Work", "vsagentbridge@vsab" },
            TimeSpan.FromSeconds(30));

        Assert.IsFalse(result.TimedOut);
        Assert.AreEqual(7, result.ExitCode);
        CollectionAssert.AreEqual(
            new[] { "[plugin]", @"[workspaceRoots=D:\My Projects;E:\Work]", "[vsagentbridge@vsab]" },
            result.Output.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("problem", result.Error.Trim());
    }

    [TestMethod]
    public void Run_TooLong_KilledAndReported()
    {
        var script = Script("slow.cmd", "@echo off", "ping -n 4 127.0.0.1 >nul", "echo done");
        var result = ForPath(script).Run(Array.Empty<string>(), TimeSpan.FromMilliseconds(300));
        Assert.IsTrue(result.TimedOut);
        Assert.AreEqual(-1, result.ExitCode);
        Assert.IsFalse(result.Output.Contains("done"));
    }
}
