using System.Text.Json.Nodes;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

// ClaudeSettingsEditor: what init reads and writes itself in ~\.claude\settings.json.
[TestClass]
internal sealed class ClaudeSettingsEditorTests
{
    private const string Output = @"D:\Tools\VSAgentBridge\Output";
    private const string PluginDir = @"D:\Tools\VSAgentBridge\claude";

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    // The shape Claude Code writes (slice 7 §3a, step 13).
    private const string Installed = """
        {
          "permissions": { "additionalDirectories": ["D:\\AI_Tools\\VSAgentBridge\\Output"] },
          "enabledPlugins": { "vsagentbridge@vsab": true },
          "extraKnownMarketplaces": {
            "vsab": { "source": { "source": "directory", "path": "D:\\Dev\\bin\\Debug\\net48\\claude" } }
          },
          "pluginConfigs": {
            "vsagentbridge@inline": { "options": { "workspaceRoots": "D:\\Inline" } },
            "vsagentbridge@vsab": { "options": { "workspaceRoots": "D:\\Hell\\MYPROGS\\VSAB_Temp;D:\\Work" } }
          }
        }
        """;

    // ---- reading ----

    [TestMethod]
    public void WorkspaceRoots_MarketplaceFirst()
    {
        Assert.AreEqual(@"D:\Hell\MYPROGS\VSAB_Temp;D:\Work", ClaudeSettingsEditor.GetWorkspaceRoots(Parse(Installed)));
    }

    [TestMethod]
    public void WorkspaceRoots_InlineWhenNoMarketplacePlugin()
    {
        var root = Parse("""{ "pluginConfigs": { "vsagentbridge@inline": { "options": { "workspaceRoots": "D:\\A" } } } }""");
        Assert.AreEqual(@"D:\A", ClaudeSettingsEditor.GetWorkspaceRoots(root));
    }

    [TestMethod]
    public void WorkspaceRoots_EmptyMarketplaceValue_InlineUsed()
    {
        var root = Parse("""
            { "pluginConfigs": {
                "vsagentbridge@vsab": { "options": { "workspaceRoots": "  " } },
                "vsagentbridge@inline": { "options": { "workspaceRoots": "D:\\A" } } } }
            """);
        Assert.AreEqual(@"D:\A", ClaudeSettingsEditor.GetWorkspaceRoots(root));
    }

    [TestMethod]
    public void WorkspaceRoots_ByPluginId()
    {
        var root = Parse(Installed);
        Assert.AreEqual(@"D:\Inline", ClaudeSettingsEditor.GetWorkspaceRoots(root, ClaudeSettingsEditor.InlinePluginId));
        Assert.AreEqual(@"D:\Hell\MYPROGS\VSAB_Temp;D:\Work", ClaudeSettingsEditor.GetWorkspaceRoots(root, ClaudeSettingsEditor.PluginId));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "pluginConfigs": {} }""")]
    [DataRow("""{ "pluginConfigs": "text" }""")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@vsab": [] } }""")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@vsab": { "options": null } } }""")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@vsab": { "options": { "workspaceRoots": 5 } } } }""")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@vsab": { "options": { "workspaceRoots": ["D:\\A"] } } } }""")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@vsab": { "options": { "workspaceRoots": "" } } } }""")]
    public void WorkspaceRoots_MissingOrUnexpected_Null(string json)
    {
        Assert.IsNull(ClaudeSettingsEditor.GetWorkspaceRoots(Parse(json)));
    }

    [TestMethod]
    public void MarketplacePath()
    {
        Assert.AreEqual(@"D:\Dev\bin\Debug\net48\claude", ClaudeSettingsEditor.GetMarketplacePath(Parse(Installed)));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "extraKnownMarketplaces": {} }""")]
    [DataRow("""{ "extraKnownMarketplaces": { "vsab": "x" } }""")]
    [DataRow("""{ "extraKnownMarketplaces": { "vsab": { "source": { "source": "github", "repo": "a/b" } } } }""")]
    public void MarketplacePath_MissingOrUnexpected_Null(string json)
    {
        Assert.IsNull(ClaudeSettingsEditor.GetMarketplacePath(Parse(json)));
    }

    [TestMethod]
    public void PluginDirs()
    {
        var root = Parse("""{ "env": { "CLAUDE_CODE_PLUGIN_DIRS": "D:\\Dev\\claude" } }""");
        Assert.AreEqual(@"D:\Dev\claude", ClaudeSettingsEditor.GetPluginDirs(root));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "env": {} }""")]
    [DataRow("""{ "env": [] }""")]
    [DataRow("""{ "env": { "CLAUDE_CODE_PLUGIN_DIRS": "" } }""")]
    [DataRow("""{ "env": { "CLAUDE_CODE_PLUGIN_DIRS": true } }""")]
    public void PluginDirs_MissingOrUnexpected_Null(string json)
    {
        Assert.IsNull(ClaudeSettingsEditor.GetPluginDirs(Parse(json)));
    }

    // ---- additionalDirectories ----

    [TestMethod]
    public void AddAdditionalDirectory_NoPermissions_Created()
    {
        var root = Parse("""{ "model": "x" }""");
        Assert.IsTrue(ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        Assert.AreEqual(Output, root["permissions"]!["additionalDirectories"]![0]!.GetValue<string>());
        Assert.AreEqual("x", root["model"]!.GetValue<string>());
    }

    [TestMethod]
    public void AddAdditionalDirectory_OtherPermissionsKept_Appended()
    {
        var root = Parse("""{ "permissions": { "defaultMode": "default", "additionalDirectories": ["E:\\Other"] } }""");
        Assert.IsTrue(ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        var list = root["permissions"]!["additionalDirectories"]!.AsArray();
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(@"E:\Other", list[0]!.GetValue<string>());
        Assert.AreEqual(Output, list[1]!.GetValue<string>());
        Assert.AreEqual("default", root["permissions"]!["defaultMode"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(@"D:\Tools\VSAgentBridge\Output")]
    [DataRow(@"d:\tools\vsagentbridge\output")]
    [DataRow(@"D:\Tools\VSAgentBridge\Output\")]
    [DataRow("D:/Tools/VSAgentBridge/Output")]
    public void AddAdditionalDirectory_AlreadyThere_Unchanged(string existing)
    {
        var root = new JsonObject
        {
            ["permissions"] = new JsonObject { ["additionalDirectories"] = new JsonArray(existing) },
        };
        Assert.IsFalse(ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        Assert.AreEqual(1, root["permissions"]!["additionalDirectories"]!.AsArray().Count);
    }

    [TestMethod]
    public void AddAdditionalDirectory_NonStringItems_Ignored()
    {
        var root = Parse("""{ "permissions": { "additionalDirectories": [1, null, {"a": 1}] } }""");
        Assert.IsTrue(ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        Assert.AreEqual(4, root["permissions"]!["additionalDirectories"]!.AsArray().Count);
    }

    [TestMethod]
    public void AddAdditionalDirectory_PermissionsNotAnObject_Throws()
    {
        var root = Parse("""{ "permissions": "all" }""");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        StringAssert.Contains(ex.Message, "'permissions' is not an object");
        Assert.AreEqual("all", root["permissions"]!.GetValue<string>());
    }

    [TestMethod]
    public void AddAdditionalDirectory_ListNotAList_Throws()
    {
        var root = Parse("""{ "permissions": { "additionalDirectories": "D:\\A" } }""");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => ClaudeSettingsEditor.AddAdditionalDirectory(root, Output));
        StringAssert.Contains(ex.Message, "'permissions.additionalDirectories' is not a list");
    }

    // ---- CLAUDE_CODE_PLUGIN_DIRS ----

    [TestMethod]
    public void SetPluginDirs_NoEnv_Created()
    {
        var root = Parse("{}");
        Assert.IsTrue(ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        Assert.AreEqual(PluginDir, root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!.GetValue<string>());
    }

    [TestMethod]
    public void SetPluginDirs_OtherVariablesKept()
    {
        var root = Parse("""{ "env": { "CLAUDE_CODE_USE_BEDROCK": "1" } }""");
        Assert.IsTrue(ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        Assert.AreEqual("1", root["env"]!["CLAUDE_CODE_USE_BEDROCK"]!.GetValue<string>());
        Assert.AreEqual(PluginDir, root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(@"D:\Tools\VSAgentBridge\claude")]
    [DataRow(@"d:\tools\vsagentbridge\claude\")]
    [DataRow("D:/Tools/VSAgentBridge/claude")]
    public void SetPluginDirs_SameFolder_Unchanged(string existing)
    {
        var root = new JsonObject { ["env"] = new JsonObject { ["CLAUDE_CODE_PLUGIN_DIRS"] = existing } };
        Assert.IsFalse(ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        Assert.AreEqual(existing, root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!.GetValue<string>());
    }

    [TestMethod]
    public void SetPluginDirs_OtherValue_ThrowsAndKeeps()
    {
        var root = Parse("""{ "env": { "CLAUDE_CODE_PLUGIN_DIRS": "D:\\Dev\\claude" } }""");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        StringAssert.Contains(ex.Message, @"D:\Dev\claude");
        StringAssert.Contains(ex.Message, "by hand");
        Assert.AreEqual(@"D:\Dev\claude", root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!.GetValue<string>());
    }

    [TestMethod]
    public void SetPluginDirs_EmptyValue_Replaced()
    {
        var root = Parse("""{ "env": { "CLAUDE_CODE_PLUGIN_DIRS": "" } }""");
        Assert.IsTrue(ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        Assert.AreEqual(PluginDir, root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!.GetValue<string>());
    }

    [TestMethod]
    public void SetPluginDirs_EnvNotAnObject_Throws()
    {
        var root = Parse("""{ "env": "x" }""");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => ClaudeSettingsEditor.SetPluginDirs(root, PluginDir));
        StringAssert.Contains(ex.Message, "'env' is not an object");
    }

    // ---- workspaceRoots for vsagentbridge@inline ----

    [TestMethod]
    public void SetInlineWorkspaceRoots_NoConfigs_Created()
    {
        var root = Parse("{}");
        Assert.IsTrue(ClaudeSettingsEditor.SetInlineWorkspaceRoots(root, @"D:\A;D:\B"));
        Assert.AreEqual(@"D:\A;D:\B", ClaudeSettingsEditor.GetWorkspaceRoots(root, ClaudeSettingsEditor.InlinePluginId));
    }

    [TestMethod]
    public void SetInlineWorkspaceRoots_Replaced_MarketplacePluginKept()
    {
        var root = Parse(Installed);
        Assert.IsTrue(ClaudeSettingsEditor.SetInlineWorkspaceRoots(root, @"D:\New"));
        Assert.AreEqual(@"D:\New", ClaudeSettingsEditor.GetWorkspaceRoots(root, ClaudeSettingsEditor.InlinePluginId));
        Assert.AreEqual(@"D:\Hell\MYPROGS\VSAB_Temp;D:\Work", ClaudeSettingsEditor.GetWorkspaceRoots(root, ClaudeSettingsEditor.PluginId));
    }

    [TestMethod]
    public void SetInlineWorkspaceRoots_OtherOptionsKept()
    {
        var root = Parse("""{ "pluginConfigs": { "vsagentbridge@inline": { "options": { "other": 1, "workspaceRoots": "D:\\Old" } } } }""");
        Assert.IsTrue(ClaudeSettingsEditor.SetInlineWorkspaceRoots(root, @"D:\New"));
        Assert.AreEqual(1, root["pluginConfigs"]!["vsagentbridge@inline"]!["options"]!["other"]!.GetValue<int>());
    }

    [TestMethod]
    public void SetInlineWorkspaceRoots_Same_Unchanged()
    {
        var root = Parse(Installed);
        Assert.IsFalse(ClaudeSettingsEditor.SetInlineWorkspaceRoots(root, @"D:\Inline"));
    }

    [TestMethod]
    [DataRow("""{ "pluginConfigs": [] }""", "'pluginConfigs' is not an object")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@inline": 1 } }""", "'pluginConfigs.vsagentbridge@inline' is not an object")]
    [DataRow("""{ "pluginConfigs": { "vsagentbridge@inline": { "options": "x" } } }""", "'pluginConfigs.vsagentbridge@inline.options' is not an object")]
    public void SetInlineWorkspaceRoots_Unexpected_Throws(string json, string message)
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => ClaudeSettingsEditor.SetInlineWorkspaceRoots(Parse(json), @"D:\A"));
        StringAssert.Contains(ex.Message, message);
    }

    // ---- with JsonFile: only the changed entry differs ----

    [TestMethod]
    public void WithJsonFile_OnlyTheNewEntryAdded()
    {
        var text = "{\n  \"model\": \"x\",\n  \"permissions\": {\n    \"additionalDirectories\": [\n      \"E:\\\\Other\"\n    ]\n  }\n}\n";
        Assert.IsTrue(JsonFile.TryParse(text, out var file, out var error), error);
        Assert.IsTrue(ClaudeSettingsEditor.AddAdditionalDirectory(file!.Root, Output));
        Assert.AreEqual(
            "{\n  \"model\": \"x\",\n  \"permissions\": {\n    \"additionalDirectories\": [\n      \"E:\\\\Other\",\n      \"D:\\\\Tools\\\\VSAgentBridge\\\\Output\"\n    ]\n  }\n}\n",
            file.Serialize());
    }
}
