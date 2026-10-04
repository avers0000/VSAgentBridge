using System.Text.Json;

namespace VSAgentBridge.Logic;

/// <summary>
/// Optional settings from vsagentbridge.json next to the exe. A missing file means defaults;
/// a broken file or value means defaults for what could not be read, plus a warning for the log —
/// a configuration mistake must never block the agent's edits.
/// </summary>
internal sealed class BridgeSettings
{
    public const string FileName = "vsagentbridge.json";

    public static BridgeSettings Default { get; } = new(LogVerbosity.Actions, logRetentionDays: 30);

    public BridgeSettings(LogVerbosity logLevel, int logRetentionDays)
    {
        LogLevel = logLevel;
        LogRetentionDays = logRetentionDays;
    }

    public LogVerbosity LogLevel { get; }

    /// <summary>Daily log files older than this many days are deleted; 0 keeps them forever.</summary>
    public int LogRetentionDays { get; }

    public static BridgeSettings Load(string directory, out string? warning)
    {
        var path = Path.Combine(directory, FileName);
        string json;
        try
        {
            if (!File.Exists(path))
            {
                warning = null;
                return Default;
            }

            json = File.ReadAllText(path); // detects a UTF-8 BOM, which Visual Studio may add when saving
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            warning = $"{FileName}: {ex.Message}; defaults used";
            return Default;
        }

        return Parse(json, out warning);
    }

    public static BridgeSettings Parse(string json, out string? warning)
    {
        JsonDocument document;
        try
        {
            // Comments and trailing commas are allowed: the file is edited by hand.
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            warning = $"{FileName}: invalid JSON ({ex.Message}); defaults used";
            return Default;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warning = $"{FileName}: the root must be a JSON object; defaults used";
                return Default;
            }

            var problems = new List<string>();

            var level = Default.LogLevel;
            if (root.TryGetProperty("logLevel", out var levelValue))
            {
                if (levelValue.ValueKind == JsonValueKind.String && TryParseLevel(levelValue.GetString(), out var parsed))
                    level = parsed;
                else
                    problems.Add("'logLevel' must be one of: off, issues, actions, all");
            }

            var days = Default.LogRetentionDays;
            if (root.TryGetProperty("logRetentionDays", out var daysValue))
            {
                if (daysValue.ValueKind == JsonValueKind.Number && daysValue.TryGetInt32(out var parsed) && parsed >= 0)
                    days = parsed;
                else
                    problems.Add("'logRetentionDays' must be a whole number, 0 or more");
            }

            warning = problems.Count == 0
                ? null
                : $"{FileName}: {string.Join("; ", problems)}; defaults used for these settings";
            return new BridgeSettings(level, days);
        }
    }

    // Enum.TryParse also accepts numbers ("7"); IsDefined rejects values outside the enum.
    private static bool TryParseLevel(string? value, out LogVerbosity level) =>
        Enum.TryParse(value, ignoreCase: true, out level) && Enum.IsDefined(typeof(LogVerbosity), level);
}