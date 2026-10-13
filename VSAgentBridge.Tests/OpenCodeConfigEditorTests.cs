using System.Text.Json.Nodes;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// OpenCodeConfigEditor: the bridge's entries in opencode.json in a workspace root (OpenCode 2.x).
[TestClass]
internal sealed class OpenCodeConfigEditorTests
{
    private const string Install = @"D:\Tools\VSAgentBridge";
    private const string OutputDir = @"D:\Tools\VSAgentBridge\Output";
    private const string Plugin = @"D:\Tools\VSAgentBridge\opencode";
    private const string Exe = @"D:\Tools\VSAgentBridge\vsagentbridge.exe";
    private const string Pattern = "D:/Tools/VSAgentBridge/Output/*";

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static bool Apply(JsonObject root, out string? replaced) =>
        OpenCodeConfigEditor.Apply(root, Install, OutputDir, out replaced);

    private static bool Apply(JsonObject root) => Apply(root, out _);

    private static string[] Plugins(JsonObject root) =>
        root["plugins"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    private static string[] Command(JsonObject root) =>
        root["mcp"]!["servers"]!["vsab"]!["command"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    // ---- new file ----

    [TestMethod]
    public void Empty_AllEntriesWritten()
    {
        var root = Parse("{}");
        Assert.IsTrue(Apply(root, out var replaced));
        Assert.IsNull(replaced);

        CollectionAssert.AreEqual(new[] { Plugin }, Plugins(root));
        var server = root["mcp"]!["servers"]!["vsab"]!;
        Assert.AreEqual("local", server["type"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { Exe, "mcp" }, Command(root));
        Assert.IsFalse(server["codemode"]!.GetValue<bool>());
        Assert.AreEqual("allow", root["permission"]!["external_directory"]![Pattern]!.GetValue<string>());
        Assert.AreEqual("ask", root["permission"]!["vsab_create_file"]!.GetValue<string>());
    }

    [TestMethod]
    public void Empty_SerializedLikeTheReadmeSample()
    {
        Assert.IsTrue(JsonFile.TryParse(null, out var file, out var error), error);
        Assert.IsTrue(Apply(file!.Root));
        var expected = string.Join("\r\n",
            "{",
            "  \"plugins\": [",
            "    \"D:\\\\Tools\\\\VSAgentBridge\\\\opencode\"",
            "  ],",
            "  \"mcp\": {",
            "    \"servers\": {",
            "      \"vsab\": {",
            "        \"type\": \"local\",",
            "        \"command\": [",
            "          \"D:\\\\Tools\\\\VSAgentBridge\\\\vsagentbridge.exe\",",
            "          \"mcp\"",
            "        ],",
            "        \"codemode\": false",
            "      }",
            "    }",
            "  },",
            "  \"permission\": {",
            "    \"external_directory\": {",
            "      \"D:/Tools/VSAgentBridge/Output/*\": \"allow\"",
            "    },",
            "    \"vsab_create_file\": \"ask\"",
            "  }",
            "}",
            "");
        Assert.AreEqual(expected, file.Serialize());
    }

    [TestMethod]
    public void SecondApply_Unchanged()
    {
        var root = Parse("{}");
        Assert.IsTrue(Apply(root));
        var once = root.ToJsonString();
        Assert.IsFalse(Apply(root, out var replaced));
        Assert.IsNull(replaced);
        Assert.AreEqual(once, root.ToJsonString());
    }

    [TestMethod]
    public void OutputDirWithTrailingSeparator_SamePattern()
    {
        var root = Parse("{}");
        OpenCodeConfigEditor.Apply(root, Install, OutputDir + "\\", out _);
        Assert.IsNotNull(root["permission"]!["external_directory"]![Pattern]);
    }

    [TestMethod]
    public void OtherEntriesKept()
    {
        var root = Parse("""
            { "model": "ollama/gpt-oss", "plugins": ["E:\\Other\\plugin"],
              "mcp": { "servers": { "other": { "type": "local", "command": ["x.exe"] } } },
              "permission": { "edit": "ask", "external_directory": { "E:/Temp/*": "allow" } } }
            """);
        Assert.IsTrue(Apply(root));
        Assert.AreEqual("ollama/gpt-oss", root["model"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { @"E:\Other\plugin", Plugin }, Plugins(root));
        Assert.IsNotNull(root["mcp"]!["servers"]!["other"]);
        Assert.AreEqual("ask", root["permission"]!["edit"]!.GetValue<string>());
        Assert.AreEqual("allow", root["permission"]!["external_directory"]!["E:/Temp/*"]!.GetValue<string>());
        Assert.AreEqual("allow", root["permission"]!["external_directory"]![Pattern]!.GetValue<string>());
    }

    // ---- plugins ----

    [TestMethod]
    [DataRow(@"D:\Tools\VSAgentBridge\opencode")]
    [DataRow(@"d:\tools\vsagentbridge\opencode\")]
    [DataRow("D:/Tools/VSAgentBridge/opencode")]
    public void Plugin_AlreadyThere_NotDuplicated(string existing)
    {
        var root = new JsonObject { ["plugins"] = new JsonArray(existing) };
        Apply(root);
        CollectionAssert.AreEqual(new[] { existing }, Plugins(root));
    }

    [TestMethod]
    public void InstallMoved_OldPluginReplacedInPlace_CommandUpdated()
    {
        var root = Parse("""
            { "plugins": ["E:\\A", "C:\\Old\\VSAB\\opencode", "E:\\B"],
              "mcp": { "servers": { "vsab": { "type": "local", "command": ["C:\\Old\\VSAB\\vsagentbridge.exe", "mcp"], "codemode": false } } } }
            """);
        Assert.IsTrue(Apply(root, out var replaced));
        Assert.AreEqual(@"C:\Old\VSAB\opencode", replaced);
        CollectionAssert.AreEqual(new[] { @"E:\A", Plugin, @"E:\B" }, Plugins(root));
        CollectionAssert.AreEqual(new[] { Exe, "mcp" }, Command(root));
    }

    [TestMethod]
    public void InstallMoved_NewPluginAlreadyThere_OldDropped()
    {
        var root = Parse("""
            { "plugins": ["C:/Old/VSAB/opencode", "D:\\Tools\\VSAgentBridge\\opencode"],
              "mcp": { "servers": { "vsab": { "type": "local", "command": ["C:/Old/VSAB/vsagentbridge.exe", "mcp"] } } } }
            """);
        Assert.IsTrue(Apply(root, out var replaced));
        Assert.AreEqual("C:/Old/VSAB/opencode", replaced);
        CollectionAssert.AreEqual(new[] { Plugin }, Plugins(root));
    }

    [TestMethod]
    public void InstallMoved_OldPluginNotInList_NewAdded()
    {
        var root = Parse("""
            { "plugins": ["E:\\A"],
              "mcp": { "servers": { "vsab": { "type": "local", "command": ["C:\\Old\\VSAB\\vsagentbridge.exe", "mcp"] } } } }
            """);
        Assert.IsTrue(Apply(root, out var replaced));
        Assert.IsNull(replaced);
        CollectionAssert.AreEqual(new[] { @"E:\A", Plugin }, Plugins(root));
    }

    [TestMethod]
    public void SameInstall_OtherSpelling_NothingReplaced()
    {
        var root = Parse("""
            { "plugins": ["d:/tools/vsagentbridge/opencode"],
              "mcp": { "servers": { "vsab": { "type": "local", "command": ["d:/tools/vsagentbridge/vsagentbridge.exe", "mcp"], "codemode": false } } },
              "permission": { "external_directory": { "D:\\Tools\\VSAgentBridge\\Output\\*": "allow" }, "vsab_create_file": "ask" } }
            """);
        Assert.IsFalse(Apply(root, out var replaced));
        Assert.IsNull(replaced);
        CollectionAssert.AreEqual(new[] { "d:/tools/vsagentbridge/opencode" }, Plugins(root));
    }

    // ---- server ----

    [TestMethod]
    [DataRow("""{ "type": "remote", "command": ["D:\\Tools\\VSAgentBridge\\vsagentbridge.exe", "mcp"] }""")]
    [DataRow("""{ "type": "local", "command": ["D:\\Tools\\VSAgentBridge\\vsagentbridge.exe"] }""")]
    [DataRow("""{ "type": "local", "command": ["D:\\Tools\\VSAgentBridge\\vsagentbridge.exe", "hook"] }""")]
    [DataRow("""{ "type": "local", "command": ["D:\\Tools\\VSAgentBridge\\vsagentbridge.exe", "mcp", "x"] }""")]
    [DataRow("""{ "type": "local", "command": "D:\\Tools\\VSAgentBridge\\vsagentbridge.exe mcp" }""")]
    [DataRow("""{ "command": [1, "mcp"] }""")]
    public void Server_WrongTypeOrCommand_Fixed(string server)
    {
        var root = Parse($$"""{ "mcp": { "servers": { "vsab": {{server}} } } }""");
        Assert.IsTrue(Apply(root));
        Assert.AreEqual("local", root["mcp"]!["servers"]!["vsab"]!["type"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { Exe, "mcp" }, Command(root));
    }

    [TestMethod]
    public void Server_UserCodemodeAndOtherKeysKept()
    {
        var root = Parse("""{ "mcp": { "servers": { "vsab": { "type": "local", "command": ["x.exe", "mcp"], "codemode": true, "timeout": 5000 } } } }""");
        Assert.IsTrue(Apply(root));
        var server = root["mcp"]!["servers"]!["vsab"]!;
        Assert.IsTrue(server["codemode"]!.GetValue<bool>());
        Assert.AreEqual(5000, server["timeout"]!.GetValue<int>());
    }

    // ---- permissions ----

    [TestMethod]
    public void Permission_UserCreateFileChoiceKept()
    {
        var root = Parse("""{ "permission": { "vsab_create_file": "allow" } }""");
        Apply(root);
        Assert.AreEqual("allow", root["permission"]!["vsab_create_file"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("D:/Tools/VSAgentBridge/Output/*")]
    [DataRow("d:/tools/vsagentbridge/output/*")]
    [DataRow(@"D:\Tools\VSAgentBridge\Output\*")]
    public void Permission_OutputPatternThere_UserValueKept(string key)
    {
        var root = new JsonObject
        {
            ["permission"] = new JsonObject { ["external_directory"] = new JsonObject { [key] = "deny" } },
        };
        Apply(root);
        var patterns = root["permission"]!["external_directory"]!.AsObject();
        Assert.AreEqual(1, patterns.Count);
        Assert.AreEqual("deny", patterns[key]!.GetValue<string>());
    }

    [TestMethod]
    public void Permission_ExternalDirectoryString_Throws()
    {
        var root = Parse("""{ "permission": { "external_directory": "ask" } }""");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => Apply(root));
        StringAssert.Contains(ex.Message, "'permission.external_directory' is not an object");
        StringAssert.Contains(ex.Message, Pattern);
        StringAssert.Contains(ex.Message, "by hand");
    }

    // ---- unexpected structure ----

    [TestMethod]
    [DataRow("""{ "plugins": "x" }""", "'plugins' is not a list")]
    [DataRow("""{ "mcp": [] }""", "'mcp' is not an object")]
    [DataRow("""{ "mcp": { "servers": "x" } }""", "'mcp.servers' is not an object")]
    [DataRow("""{ "mcp": { "servers": { "vsab": true } } }""", "'mcp.servers.vsab' is not an object")]
    [DataRow("""{ "permission": "allow" }""", "'permission' is not an object")]
    public void Unexpected_Throws(string json, string message)
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => Apply(Parse(json)));
        StringAssert.Contains(ex.Message, message);
    }

    // ---- GetInstallDirectory ----

    [TestMethod]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": ["C:\\Old\\VSAB\\vsagentbridge.exe", "mcp"] } } } }""", @"C:\Old\VSAB")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": ["C:/Old/VSAB/vsagentbridge.exe", "mcp"] } } } }""", @"C:\Old\VSAB")]
    public void InstallDirectory_FromCommand(string json, string expected)
    {
        Assert.AreEqual(expected, OpenCodeConfigEditor.GetInstallDirectory(Parse(json)));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "mcp": { "servers": {} } }""")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": "x.exe mcp" } } } }""")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": [] } } } }""")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": [5, "mcp"] } } } }""")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": ["  ", "mcp"] } } } }""")]
    [DataRow("""{ "mcp": { "servers": { "vsab": { "command": ["C:\\Bad|Name\\vsagentbridge.exe", "mcp"] } } } }""")]
    [DataRow("""{ "mcp": "x" }""")]
    public void InstallDirectory_NoneOrUnexpected_Null(string json)
    {
        Assert.IsNull(OpenCodeConfigEditor.GetInstallDirectory(Parse(json)));
    }
}
