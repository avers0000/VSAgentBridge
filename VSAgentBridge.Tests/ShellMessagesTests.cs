using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class ShellMessagesTests
{
    private const string Record = @"D:\Tools\VSAB\Output\ses_1\_pending-operations.txt";

    [TestMethod]
    [DataRow(ShellOperation.Write, "writes to files")]
    [DataRow(ShellOperation.Copy, "copies files")]
    [DataRow(ShellOperation.ClearReadOnly, "makes files writable")]
    [DataRow(ShellOperation.Delete, "deletes files")]
    [DataRow(ShellOperation.Move, "moves or renames files")]
    public void Verb_PerOperation(ShellOperation operation, string expected)
    {
        Assert.AreEqual(expected, ShellMessages.Verb(operation));
    }

    [TestMethod]
    public void Describe_One()
    {
        Assert.AreEqual("it deletes files (Remove-Item)", ShellMessages.Describe(Findings("Remove-Item x")));
    }

    [TestMethod]
    public void Describe_GroupsByOperation_InOrder_CommandsOnce()
    {
        var findings = Findings("rm a; echo x > b; Set-Content c 'y'; del d; mv e f; RM g");

        Assert.AreEqual(
            "it deletes files (rm, del), writes to files (>, Set-Content) and moves or renames files (mv)",
            ShellMessages.Describe(findings));
    }

    [TestMethod]
    public void DeleteOrMoveCommand_DescribesAndNamesTheRecordOnce()
    {
        var text = ShellMessages.DeleteOrMoveCommand(Findings("Remove-Item a; mv b c"), Record);

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked this command: it deletes files (Remove-Item) and moves or renames files (mv). ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("Nothing was run."), text);
        AssertDeleteOrMoveRest(text);
        Assert.IsTrue(text.EndsWith($"; VS Agent Bridge recorded the request in {Record}.", StringComparison.Ordinal), text);
        AssertOnce(text, Record);
    }

    [TestMethod]
    public void DeleteOrMoveCommand_NoRecord_EndsWithoutIt()
    {
        var text = ShellMessages.DeleteOrMoveCommand(Findings("del x"), recordPath: null);

        Assert.IsTrue(text.EndsWith("the SSDT Rename refactoring).", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("recorded"), text);
    }

    [TestMethod]
    [DataRow(ShellOperation.Delete, "VS Agent Bridge blocked this patch: it deletes files.")]
    [DataRow(ShellOperation.Move, "VS Agent Bridge blocked this patch: it moves or renames files.")]
    public void DeleteOrMovePatch_SaysNothingWasApplied(ShellOperation operation, string start)
    {
        var text = ShellMessages.DeleteOrMovePatch(operation, Record);

        Assert.IsTrue(text.StartsWith(start, StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("Nothing in the patch was applied"), text);
        Assert.IsTrue(text.Contains("without the delete and move parts"), text);
        AssertDeleteOrMoveRest(text);
        AssertOnce(text, Record);
    }

    [TestMethod]
    public void WriteCommand_SendsToTheTools()
    {
        var text = ShellMessages.WriteCommand(Findings("echo a > x; Copy-Item y z; attrib -R w"));

        Assert.IsTrue(text.StartsWith("VS Agent Bridge blocked this command: it writes to files (>), copies files (Copy-Item) and makes files writable (attrib). Nothing was run.", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("use the write tool to create a file and the edit tool to change one"), text);
        Assert.IsFalse(text.Contains("tell the user"), text); // nothing for the user: the model redoes it with the tools
    }

    [TestMethod]
    public void InternalError_IsADenialForTheUser()
    {
        Assert.IsTrue(ShellMessages.InternalError.StartsWith("VS Agent Bridge blocked this command:", StringComparison.Ordinal));
        Assert.IsTrue(ShellMessages.InternalError.Contains("Do not retry it with any tool."));
        Assert.IsTrue(ShellMessages.InternalError.Contains("details are in its log"));
    }

    private static IReadOnlyList<ShellFinding> Findings(string command) => ShellCommandScanner.Scan(command);

    private static void AssertDeleteOrMoveRest(string text)
    {
        Assert.IsTrue(text.Contains("Deleting, moving and renaming files is done only by the user, in Visual Studio."), text);
        Assert.IsTrue(text.Contains("Do not retry it and do not do it another way"), text);
        Assert.IsTrue(text.Contains("recreating them under new names"), text);
        Assert.IsTrue(text.Contains("do not change references to these files yet"), text);
        Assert.IsTrue(text.Contains("At the end of your answer tell the user which files to delete, move or rename and where they are referenced"), text);
    }

    private static void AssertOnce(string text, string part)
    {
        var first = text.IndexOf(part, StringComparison.Ordinal);
        Assert.IsTrue(first >= 0, text);
        Assert.AreEqual(-1, text.IndexOf(part, first + 1, StringComparison.Ordinal), text);
    }
}
