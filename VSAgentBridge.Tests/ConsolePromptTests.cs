using VSAgentBridge.Init;

namespace VSAgentBridge.Tests;

// ConsolePrompt: init's questions on a given input; at the end of input nothing is answered "yes".
[TestClass]
internal sealed class ConsolePromptTests
{
    private static (ConsolePrompt Prompt, StringWriter Output) Create(string input)
    {
        var output = new StringWriter();
        return (new ConsolePrompt(new StringReader(input), output), output);
    }

    // ---- AskLine ----

    [TestMethod]
    public void AskLine_QuestionWrittenAnswerTrimmed()
    {
        var (prompt, output) = Create("  D:\\A;D:\\B  \n");
        Assert.AreEqual(@"D:\A;D:\B", prompt.AskLine("Workspace roots:"));
        Assert.AreEqual("Workspace roots: ", output.ToString());
    }

    [TestMethod]
    public void AskLine_EndOfInput_Null()
    {
        Assert.IsNull(Create("").Prompt.AskLine("Q:"));
    }

    [TestMethod]
    public void AskLine_EmptyLine_Empty()
    {
        Assert.AreEqual("", Create("\n").Prompt.AskLine("Q:"));
    }

    // ---- AskYesNo ----

    [TestMethod]
    [DataRow("\n", true, true)]
    [DataRow("\n", false, false)]
    [DataRow("y\n", false, true)]
    [DataRow("Y\n", false, true)]
    [DataRow("yes\n", false, true)]
    [DataRow(" YES \n", false, true)]
    [DataRow("n\n", true, false)]
    [DataRow("N\n", true, false)]
    [DataRow("no\n", true, false)]
    public void YesNo(string input, bool defaultYes, bool expected)
    {
        Assert.AreEqual(expected, Create(input).Prompt.AskYesNo("Apply?", defaultYes));
    }

    [TestMethod]
    public void YesNo_Hint()
    {
        var (yes, yesOutput) = Create("\n");
        yes.AskYesNo("Apply?");
        Assert.AreEqual("Apply? [Y/n] ", yesOutput.ToString());

        var (no, noOutput) = Create("\n");
        no.AskYesNo("Apply?", defaultYes: false);
        Assert.AreEqual("Apply? [y/N] ", noOutput.ToString());
    }

    [TestMethod]
    public void YesNo_NotUnderstood_AskedAgain()
    {
        var (prompt, output) = Create("maybe\nda\ny\n");
        Assert.IsTrue(prompt.AskYesNo("Apply?"));
        Assert.AreEqual(2, output.ToString().Split(new[] { "Please answer y or n." }, StringSplitOptions.None).Length - 1);
        Assert.AreEqual(3, output.ToString().Split(new[] { "Apply? [Y/n] " }, StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void YesNo_EndOfInput_NoEvenWithDefaultYes()
    {
        Assert.IsFalse(Create("").Prompt.AskYesNo("Apply?", defaultYes: true));
    }

    [TestMethod]
    public void YesNo_EndOfInputAfterWrongAnswer_No()
    {
        Assert.IsFalse(Create("what\n").Prompt.AskYesNo("Apply?"));
    }

    // ---- AskChoice ----

    private static readonly string[] Choices = { "First way", "Second way", "Stop" };

    [TestMethod]
    [DataRow("1\n", 1)]
    [DataRow("2\n", 2)]
    [DataRow(" 3 \n", 3)]
    public void Choice(string input, int expected)
    {
        Assert.AreEqual(expected, Create(input).Prompt.AskChoice(Choices));
    }

    [TestMethod]
    public void Choice_ListAndQuestionWritten()
    {
        var (prompt, output) = Create("1\n");
        prompt.AskChoice(Choices);
        var nl = Environment.NewLine;
        Assert.AreEqual($"  1. First way{nl}  2. Second way{nl}  3. Stop{nl}Choose [1/2/3]: ", output.ToString());
    }

    [TestMethod]
    public void Choice_NoDefault_WrongAnswersAskedAgain()
    {
        var (prompt, output) = Create("\n0\n4\nx\n2\n");
        Assert.AreEqual(2, prompt.AskChoice(Choices));
        Assert.AreEqual(4, output.ToString().Split(new[] { "Please type a number from 1 to 3." }, StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void Choice_EndOfInput_Null()
    {
        Assert.IsNull(Create("").Prompt.AskChoice(Choices));
        Assert.IsNull(Create("9\n").Prompt.AskChoice(Choices));
    }
}
