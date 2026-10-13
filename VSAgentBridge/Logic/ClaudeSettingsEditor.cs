using System.Text.Json.Nodes;
using static VSAgentBridge.Logic.JsonTree;

namespace VSAgentBridge.Logic;

/// <summary>
/// The entries of Claude Code's user settings (~\.claude\settings.json) that init reads and writes itself.
/// The marketplace, the enabled plugin and its "workspaceRoots" for vsagentbridge@vsab are written by
/// "claude plugin" commands instead. Works on the tree of a <see cref="JsonFile"/>; reports a structure it
/// cannot change with <see cref="InvalidDataException"/>, so that the caller leaves the file untouched.
/// </summary>
internal static class ClaudeSettingsEditor
{
    public const string MarketplaceName = "vsab";
    public const string PluginId = "vsagentbridge@vsab";
    public const string InlinePluginId = "vsagentbridge@inline";
    public const string PluginDirsVariable = "CLAUDE_CODE_PLUGIN_DIRS";

    /// <summary>
    /// The current workspace roots: those of the marketplace plugin if set, otherwise those of the plugin
    /// loaded through CLAUDE_CODE_PLUGIN_DIRS; null if neither is set.
    /// </summary>
    public static string? GetWorkspaceRoots(JsonObject root) =>
        GetWorkspaceRoots(root, PluginId) ?? GetWorkspaceRoots(root, InlinePluginId);

    /// <summary>pluginConfigs."&lt;pluginId&gt;".options.workspaceRoots, or null if it is missing, empty or not a string.</summary>
    public static string? GetWorkspaceRoots(JsonObject root, string pluginId)
    {
        var value = GetString(Find(root, "pluginConfigs", pluginId, "options", "workspaceRoots"));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The folder the "vsab" marketplace points to (extraKnownMarketplaces.vsab.source.path), or null.</summary>
    public static string? GetMarketplacePath(JsonObject root) =>
        GetString(Find(root, "extraKnownMarketplaces", MarketplaceName, "source", "path"));

    /// <summary>The value of env.CLAUDE_CODE_PLUGIN_DIRS, or null if it is not set.</summary>
    public static string? GetPluginDirs(JsonObject root)
    {
        var value = GetString(Find(root, "env", PluginDirsVariable));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Adds <paramref name="directory"/> to permissions.additionalDirectories unless it is there already.
    /// Returns true if the tree changed.
    /// </summary>
    public static bool AddAdditionalDirectory(JsonObject root, string directory)
    {
        var permissions = GetOrCreateObject(root, "permissions", "permissions");
        var list = GetOrCreateArray(permissions, "additionalDirectories", "permissions.additionalDirectories");

        if (list.Any(item => GetString(item) is { } existing && PathUtil.SamePath(existing, directory)))
            return false;

        list.Add(directory);
        return true;
    }

    /// <summary>
    /// Sets env.CLAUDE_CODE_PLUGIN_DIRS to <paramref name="pluginDirectory"/> (the alternative to the marketplace).
    /// Returns false if it is set to this folder already; throws if it is set to something else,
    /// because a value written by someone else is not replaced.
    /// </summary>
    public static bool SetPluginDirs(JsonObject root, string pluginDirectory)
    {
        var env = GetOrCreateObject(root, "env", "env");
        var current = GetPluginDirs(root);
        if (current == null)
        {
            env[PluginDirsVariable] = pluginDirectory;
            return true;
        }

        if (PathUtil.SamePath(current, pluginDirectory))
            return false;

        throw new InvalidDataException(
            $"'env.{PluginDirsVariable}' is already set to \"{current}\"; add \"{pluginDirectory}\" to it by hand");
    }

    /// <summary>
    /// Sets pluginConfigs."vsagentbridge@inline".options.workspaceRoots (the alternative to the marketplace;
    /// with the marketplace, "claude plugin install --config" writes it). Returns true if the tree changed.
    /// </summary>
    public static bool SetInlineWorkspaceRoots(JsonObject root, string workspaceRoots)
    {
        var configs = GetOrCreateObject(root, "pluginConfigs", "pluginConfigs");
        var plugin = GetOrCreateObject(configs, InlinePluginId, $"pluginConfigs.{InlinePluginId}");
        var options = GetOrCreateObject(plugin, "options", $"pluginConfigs.{InlinePluginId}.options");

        if (GetString(options["workspaceRoots"]) == workspaceRoots)
            return false;

        options["workspaceRoots"] = workspaceRoots;
        return true;
    }
}