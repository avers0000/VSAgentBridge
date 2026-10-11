using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class McpMessagesTests
{
    [TestMethod]
    public void UseWrite_SaysNothingWasCreatedAndNamesTheWriteTool()
    {
        var text = McpMessages.UseWrite("no open Visual Studio solution contains this folder.");

        Assert.IsTrue(text.StartsWith("VS Agent Bridge did not create this file: no open Visual Studio solution contains this folder. ", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("Nothing was created."), text);
        Assert.IsTrue(text.Contains("with the write tool"), text);
    }

    [TestMethod]
    [DataRow(McpMessages.InOutput)]
    [DataRow(McpMessages.OutsideRoots)]
    [DataRow(McpMessages.InternalError)]
    public void Causes_AreSentencesForUseWrite(string cause)
    {
        Assert.IsTrue(cause.EndsWith(".", StringComparison.Ordinal), cause);
        Assert.IsTrue(char.IsLower(cause[0]), cause);
    }

    [TestMethod]
    public void WriteFailed_NamesTheError()
    {
        var cause = McpMessages.WriteFailed("disk full");

        Assert.AreEqual("writing the file failed (disk full).", cause);
    }

    [TestMethod]
    public void AlreadyExists_PointsToTheEditTool()
    {
        Assert.IsTrue(McpMessages.AlreadyExists.Contains("Change it with the edit tool."));
        Assert.IsFalse(McpMessages.AlreadyExists.Contains("write tool"));
    }

    [TestMethod]
    public void FolderExistsAndInvalidPath_DoNotSendToTheWriteTool()
    {
        Assert.IsFalse(McpMessages.FolderExists.Contains("write tool"));
        var invalid = McpMessages.InvalidPath("Illegal characters in path.");
        Assert.IsTrue(invalid.Contains("Illegal characters in path."), invalid);
        Assert.IsFalse(invalid.Contains("write tool"), invalid);
    }

    [TestMethod]
    public void Description_NamesBothFallbacks()
    {
        Assert.IsTrue(McpMessages.CreateFileDescription.Contains("edit tool"));
        Assert.IsTrue(McpMessages.CreateFileDescription.Contains("write tool"));
    }

    [TestMethod]
    public void Texts_DoNotMentionTfvc()
    {
        foreach (var text in new[]
                 {
                     McpMessages.CreateFileDescription, McpMessages.PathDescription, McpMessages.ContentDescription,
                     McpMessages.AlreadyExists, McpMessages.FolderExists, McpMessages.UseWrite("x.")
                 })
            Assert.IsFalse(text.Contains("TFVC") || text.Contains("TFS"), text);
    }
}
