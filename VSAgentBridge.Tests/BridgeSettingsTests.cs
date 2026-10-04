using System.Text;
using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class BridgeSettingsTests
{
    private string _folder = "";

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "VSAgentBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void Default_IsActionsAnd30Days()
    {
        Assert.AreEqual(LogVerbosity.Actions, BridgeSettings.Default.LogLevel);
        Assert.AreEqual(30, BridgeSettings.Default.LogRetentionDays);
    }

    [TestMethod]
    public void Parse_EmptyObject_DefaultsWithoutWarning()
    {
        var settings = BridgeSettings.Parse("{}", out var warning);

        Assert.IsNull(warning);
        Assert.AreEqual(LogVerbosity.Actions, settings.LogLevel);
        Assert.AreEqual(30, settings.LogRetentionDays);
    }

    [TestMethod]
    [DataRow("off", LogVerbosity.Off)]
    [DataRow("issues", LogVerbosity.Issues)]
    [DataRow("actions", LogVerbosity.Actions)]
    [DataRow("all", LogVerbosity.All)]
    [DataRow("ALL", LogVerbosity.All)]
    [DataRow("Issues", LogVerbosity.Issues)]
    public void Parse_LogLevel_KnownValues(string value, LogVerbosity expected)
    {
        var settings = BridgeSettings.Parse($$"""{"logLevel":"{{value}}"}""", out var warning);

        Assert.IsNull(warning);
        Assert.AreEqual(expected, settings.LogLevel);
    }

    [TestMethod]
    [DataRow("0", 0)]
    [DataRow("7", 7)]
    [DataRow("365", 365)]
    public void Parse_RetentionDays_ValidValues(string value, int expected)
    {
        var settings = BridgeSettings.Parse($$"""{"logRetentionDays":{{value}}}""", out var warning);

        Assert.IsNull(warning);
        Assert.AreEqual(expected, settings.LogRetentionDays);
    }

    [TestMethod]
    public void Parse_CommentsAndTrailingCommas_Allowed()
    {
        var json = """
            // comment
            {
              "logLevel": "all", // trailing comment
              /* block */
              "logRetentionDays": 5,
            }
            """;

        var settings = BridgeSettings.Parse(json, out var warning);

        Assert.IsNull(warning);
        Assert.AreEqual(LogVerbosity.All, settings.LogLevel);
        Assert.AreEqual(5, settings.LogRetentionDays);
    }

    [TestMethod]
    [DataRow("\"verbose\"", DisplayName = "unknown name")]
    [DataRow("\"7\"", DisplayName = "number outside the enum as string")]
    [DataRow("\"\"", DisplayName = "empty string")]
    [DataRow("2", DisplayName = "number instead of string")]
    [DataRow("null", DisplayName = "null")]
    public void Parse_InvalidLogLevel_DefaultLevelKeepsOtherSettingAndWarns(string value)
    {
        var settings = BridgeSettings.Parse($$"""{"logLevel":{{value}},"logRetentionDays":3}""", out var warning);

        Assert.AreEqual(LogVerbosity.Actions, settings.LogLevel);
        Assert.AreEqual(3, settings.LogRetentionDays);
        Assert.IsNotNull(warning);
        Assert.IsTrue(warning.Contains("'logLevel'"), warning);
        Assert.IsFalse(warning.Contains("'logRetentionDays'"), warning);
    }

    [TestMethod]
    [DataRow("-1", DisplayName = "negative")]
    [DataRow("1.5", DisplayName = "fraction")]
    [DataRow("\"30\"", DisplayName = "string")]
    [DataRow("99999999999", DisplayName = "too large")]
    public void Parse_InvalidRetentionDays_DefaultDaysKeepsOtherSettingAndWarns(string value)
    {
        var settings = BridgeSettings.Parse($$"""{"logLevel":"issues","logRetentionDays":{{value}}}""", out var warning);

        Assert.AreEqual(LogVerbosity.Issues, settings.LogLevel);
        Assert.AreEqual(30, settings.LogRetentionDays);
        Assert.IsNotNull(warning);
        Assert.IsTrue(warning.Contains("'logRetentionDays'"), warning);
    }

    [TestMethod]
    public void Parse_BothInvalid_WarningListsBoth()
    {
        BridgeSettings.Parse("""{"logLevel":"x","logRetentionDays":-5}""", out var warning);

        Assert.IsNotNull(warning);
        Assert.IsTrue(warning.Contains("'logLevel'") && warning.Contains("'logRetentionDays'"), warning);
    }

    [TestMethod]
    [DataRow("not json", "invalid JSON", DisplayName = "not JSON")]
    [DataRow("[1]", "root must be a JSON object", DisplayName = "array root")]
    [DataRow("", "invalid JSON", DisplayName = "empty")]
    public void Parse_BrokenFile_DefaultsAndWarns(string json, string expectedInWarning)
    {
        var settings = BridgeSettings.Parse(json, out var warning);

        Assert.AreSame(BridgeSettings.Default, settings);
        Assert.IsNotNull(warning);
        Assert.IsTrue(warning.Contains(expectedInWarning), warning);
    }

    [TestMethod]
    public void Parse_UnknownKeys_Ignored()
    {
        BridgeSettings.Parse("""{"outputPath":"D:\\x","logLevel":"all"}""", out var warning);

        Assert.IsNull(warning);
    }

    [TestMethod]
    public void Load_NoFile_DefaultsWithoutWarning()
    {
        var settings = BridgeSettings.Load(_folder, out var warning);

        Assert.AreSame(BridgeSettings.Default, settings);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public void Load_FileWithUtf8Bom_IsRead()
    {
        File.WriteAllText(Path.Combine(_folder, BridgeSettings.FileName), """{"logLevel":"off"}""",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var settings = BridgeSettings.Load(_folder, out var warning);

        Assert.IsNull(warning);
        Assert.AreEqual(LogVerbosity.Off, settings.LogLevel);
    }

    [TestMethod]
    public void Load_LockedFile_DefaultsAndWarns()
    {
        var path = Path.Combine(_folder, BridgeSettings.FileName);
        File.WriteAllText(path, """{"logLevel":"off"}""");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var settings = BridgeSettings.Load(_folder, out var warning);

            Assert.AreSame(BridgeSettings.Default, settings);
            Assert.IsNotNull(warning);
        }
    }
}
