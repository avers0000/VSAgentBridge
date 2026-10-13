using VSAgentBridge.Init;

namespace VSAgentBridge.Tests;

// InitOptions: the command line after "vsagentbridge init".
[TestClass]
internal sealed class InitOptionsTests
{
    private static InitOptions ParseOk(params string[] args)
    {
        Assert.IsTrue(InitOptions.TryParse(args, out var options, out var error), error);
        Assert.IsNull(error);
        return options!;
    }

    private static string ParseError(params string[] args)
    {
        Assert.IsFalse(InitOptions.TryParse(args, out var options, out var error));
        Assert.IsNull(options);
        Assert.IsNotNull(error);
        return error!;
    }

    [TestMethod]
    [DataRow("claude", "claude")]
    [DataRow("CLAUDE", "claude")]
    [DataRow("opencode", "opencode")]
    [DataRow("OpenCode", "opencode")]
    public void Harness_Defaults(string arg, string harness)
    {
        var options = ParseOk(arg);
        Assert.AreEqual(harness, options.Harness);
        Assert.IsFalse(options.GlobalRules);
        Assert.IsNull(options.Roots);
        Assert.IsFalse(options.NoMarketplace);
    }

    [TestMethod]
    public void AllOptions_Claude()
    {
        var options = ParseOk("claude", "-g", "--roots", @"D:\A;D:\My B", "--no-marketplace");
        Assert.IsTrue(options.GlobalRules);
        Assert.AreEqual(@"D:\A;D:\My B", options.Roots);
        Assert.IsTrue(options.NoMarketplace);
    }

    [TestMethod]
    public void OptionsInAnyOrder_CaseInsensitive()
    {
        var options = ParseOk("opencode", "--ROOTS", @"D:\A", "--Global");
        Assert.IsTrue(options.GlobalRules);
        Assert.AreEqual(@"D:\A", options.Roots);
    }

    [TestMethod]
    public void RootsWithEquals()
    {
        Assert.AreEqual(@"D:\A;D:\B", ParseOk("claude", @"--roots=D:\A;D:\B").Roots);
    }

    [TestMethod]
    public void RootsWithEquals_Empty_KeptForWorkspaceRootsToReject()
    {
        Assert.AreEqual("", ParseOk("claude", "--roots=").Roots);
    }

    [TestMethod]
    public void NoAgent_Error()
    {
        StringAssert.Contains(ParseError(), "claude or opencode");
    }

    [TestMethod]
    [DataRow("vscode")]
    [DataRow("-g")]
    [DataRow("--roots")]
    public void UnknownAgent_Error(string agent)
    {
        StringAssert.Contains(ParseError(agent), $"Unknown agent \"{agent}\"");
    }

    [TestMethod]
    public void NoMarketplace_OpenCode_Error()
    {
        StringAssert.Contains(ParseError("opencode", "--no-marketplace"), "claude only");
    }

    [TestMethod]
    [DataRow("claude|--roots")]
    [DataRow("claude|--roots|-g")]
    [DataRow("claude|--roots|--no-marketplace")]
    public void RootsWithoutValue_Error(string args)
    {
        StringAssert.Contains(ParseError(args.Split('|')), "--roots needs a value");
    }

    [TestMethod]
    [DataRow("claude|-g|-g")]
    [DataRow("claude|-g|--global")]
    [DataRow(@"claude|--roots|D:\A|--roots=D:\B")]
    [DataRow("claude|--no-marketplace|--no-marketplace")]
    public void Twice_Error(string args)
    {
        StringAssert.Contains(ParseError(args.Split('|')), "is given twice");
    }

    [TestMethod]
    [DataRow("claude|--force")]
    [DataRow(@"claude|D:\A")]
    [DataRow("claude|-g=1")]
    [DataRow("claude|--no-marketplace=yes")]
    [DataRow("claude|opencode")]
    public void Unknown_Error(string args)
    {
        var list = args.Split('|');
        StringAssert.Contains(ParseError(list), $"Unknown option \"{list[1]}\"");
    }

    [TestMethod]
    public void Usage_NamesBothAgents()
    {
        StringAssert.Contains(InitOptions.Usage, "init claude");
        StringAssert.Contains(InitOptions.Usage, "init opencode");
    }
}
