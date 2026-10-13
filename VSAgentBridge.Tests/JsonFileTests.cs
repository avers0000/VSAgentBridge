using System.Text.Json.Nodes;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// JsonFile: agent settings files (settings.json, opencode.json) read as a tree and written back by init.
[TestClass]
internal sealed class JsonFileTests
{
    private static JsonFile ParseOk(string? text)
    {
        Assert.IsTrue(JsonFile.TryParse(text, out var file, out var error), error);
        Assert.IsNull(error);
        Assert.IsNotNull(file);
        return file!;
    }

    private static string ParseError(string text)
    {
        Assert.IsFalse(JsonFile.TryParse(text, out var file, out var error));
        Assert.IsNull(file);
        Assert.IsNotNull(error);
        return error!;
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  \r\n ")]
    public void Empty_NewObject(string? text)
    {
        var file = ParseOk(text);
        Assert.AreEqual(0, file.Root.Count);
        Assert.IsFalse(file.HadComments);
        Assert.AreEqual("\r\n", file.NewLine);
        Assert.IsTrue(file.FinalNewline);
        Assert.AreEqual("{}\r\n", file.Serialize());
    }

    [TestMethod]
    public void Lf_RoundTripUnchanged()
    {
        var text = "{\n  \"a\": 1,\n  \"b\": \"x\",\n  \"c\": {\n    \"d\": [\n      true,\n      null\n    ]\n  }\n}\n";
        var file = ParseOk(text);
        Assert.AreEqual("\n", file.NewLine);
        Assert.AreEqual(text, file.Serialize());
    }

    [TestMethod]
    public void Crlf_RoundTripUnchanged()
    {
        var text = "{\r\n  \"a\": 1,\r\n  \"b\": [\r\n    \"x\"\r\n  ]\r\n}\r\n";
        var file = ParseOk(text);
        Assert.AreEqual("\r\n", file.NewLine);
        Assert.AreEqual(text, file.Serialize());
    }

    [TestMethod]
    public void NoFinalNewline_Kept()
    {
        var text = "{\n  \"a\": 1\n}";
        var file = ParseOk(text);
        Assert.IsFalse(file.FinalNewline);
        Assert.AreEqual(text, file.Serialize());
    }

    [TestMethod]
    public void OneLine_IndentedWithCrlf()
    {
        var file = ParseOk("{\"a\":1,\"b\":[1,2]}");
        Assert.AreEqual("{\r\n  \"a\": 1,\r\n  \"b\": [\r\n    1,\r\n    2\r\n  ]\r\n}", file.Serialize());
    }

    [TestMethod]
    public void FourSpaceIndent_WrittenWithTwo()
    {
        var file = ParseOk("{\n    \"a\": {\n        \"b\": 1\n    }\n}\n");
        Assert.AreEqual("{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", file.Serialize());
    }

    [TestMethod]
    public void KeyOrder_Kept_NewKeyLast()
    {
        var file = ParseOk("{\n  \"z\": 1,\n  \"a\": 2,\n  \"m\": 3\n}\n");
        file.Root["a"] = 20;
        file.Root["b"] = 4;
        Assert.AreEqual("{\n  \"z\": 1,\n  \"a\": 20,\n  \"m\": 3,\n  \"b\": 4\n}\n", file.Serialize());
    }

    [TestMethod]
    public void NestedChange()
    {
        var file = ParseOk("{\n  \"permissions\": {\n    \"additionalDirectories\": []\n  }\n}\n");
        file.Root["permissions"]!["additionalDirectories"]!.AsArray().Add(@"D:\Tools\VSAgentBridge\Output");
        Assert.AreEqual(
            "{\n  \"permissions\": {\n    \"additionalDirectories\": [\n      \"D:\\\\Tools\\\\VSAgentBridge\\\\Output\"\n    ]\n  }\n}\n",
            file.Serialize());
    }

    [TestMethod]
    public void Numbers_KeptAsWritten()
    {
        var text = "{\n  \"a\": 1.0,\n  \"b\": 1e3,\n  \"c\": -0.5\n}\n";
        Assert.AreEqual(text, ParseOk(text).Serialize());
    }

    [TestMethod]
    public void Backslashes_Kept()
    {
        var text = "{\n  \"path\": \"D:\\\\Hell\\\\MYPROGS\"\n}\n";
        var file = ParseOk(text);
        Assert.AreEqual(@"D:\Hell\MYPROGS", file.Root["path"]!.GetValue<string>());
        Assert.AreEqual(text, file.Serialize());
    }

    [TestMethod]
    public void SpecialAndNonAsciiCharacters_NotEscaped()
    {
        var text = "{\n  \"a\": \"<b> & c + d\",\n  \"b\": \"caf\u00e9 \u65e5\u672c\",\n  \"c\": \"it's\"\n}\n";
        Assert.AreEqual(text, ParseOk(text).Serialize());
    }

    [TestMethod]
    public void NewNonAsciiValue_NotEscaped()
    {
        var file = ParseOk("{}");
        file.Root["a"] = "caf\u00e9 & <x>";
        Assert.AreEqual("{\r\n  \"a\": \"caf\u00e9 & <x>\"\r\n}", file.Serialize());
    }

    [TestMethod]
    public void Comments_AllowedAndReported_DroppedOnWrite()
    {
        var file = ParseOk("{\n  // line comment\n  \"a\": 1, /* block */\n  \"b\": 2\n}\n");
        Assert.IsTrue(file.HadComments);
        Assert.AreEqual("{\n  \"a\": 1,\n  \"b\": 2\n}\n", file.Serialize());
    }

    [TestMethod]
    public void TrailingCommas_Allowed_NotComments()
    {
        var file = ParseOk("{\n  \"a\": [1, 2,],\n  \"b\": 2,\n}\n");
        Assert.IsFalse(file.HadComments);
        Assert.AreEqual("{\n  \"a\": [\n    1,\n    2\n  ],\n  \"b\": 2\n}\n", file.Serialize());
    }

    [TestMethod]
    public void SlashesInsideAString_NotComments()
    {
        var file = ParseOk("{\n  \"url\": \"https://example.com/*x*/\"\n}\n");
        Assert.IsFalse(file.HadComments);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"a\": }")]
    [DataRow("{\"a\": 1} x")]
    [DataRow("not json")]
    [DataRow("// only a comment")]
    public void InvalidJson_Error(string text)
    {
        StringAssert.StartsWith(ParseError(text), "invalid JSON");
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("[{\"a\": 1}]")]
    [DataRow("\"text\"")]
    [DataRow("42")]
    [DataRow("null")]
    public void RootNotAnObject_Error(string text)
    {
        Assert.AreEqual("the root must be a JSON object", ParseError(text));
    }

    [TestMethod]
    public void DuplicateKey_TopLevel_Error()
    {
        StringAssert.StartsWith(ParseError("{\"a\": 1, \"a\": 2}"), "invalid JSON");
    }

    [TestMethod]
    public void DuplicateKey_Nested_Error()
    {
        StringAssert.StartsWith(ParseError("{\"x\": {\"a\": 1, \"a\": 2}}"), "invalid JSON");
    }
}
