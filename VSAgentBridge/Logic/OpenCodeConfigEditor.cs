using System.Text.Json.Nodes;
using static VSAgentBridge.Logic.JsonTree;

namespace VSAgentBridge.Logic;

/// <summary>
/// The bridge's entries in an opencode.json in a workspace root (OpenCode 2.x format): the plugin, the MCP
/// server "vsab", access to the Output folder and the permission for vsab_create_file. Works on the tree of a
/// <see cref="JsonFile"/>. When it throws <see cref="InvalidDataException"/>, the tree may be partly changed:
/// the caller must not write it.
/// </summary>
internal static class OpenCodeConfigEditor
{
    public const string ServerName = "vsab";
    public const string CreateFilePermission = "vsab_create_file";
    public const string PluginFolderName = "opencode";
    public const string ExeFileName = "vsagentbridge.exe";

    /// <summary>
    /// The install folder an earlier setup wrote: the folder of mcp.servers.vsab.command[0];
    /// null if there is no such entry or it is not a path.
    /// </summary>
    public static string? GetInstallDirectory(JsonObject root)
    {
        if (Find(root, "mcp", "servers", ServerName, "command") is not JsonArray command || command.Count == 0)
            return null;

        var exe = GetString(command[0]);
        if (string.IsNullOrWhiteSpace(exe))
            return null;

        try
        {
            return Path.GetDirectoryName(exe!.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the bridge's entries for the install folder <paramref name="installDirectory"/> and the
    /// Output folder <paramref name="outputDirectory"/>. Returns true if the tree changed.
    /// <paramref name="replacedPlugin"/> is the plugin entry of an earlier install folder that was replaced,
    /// so that OpenCode does not load the bridge's plugin twice; null if none.
    /// </summary>
    public static bool Apply(JsonObject root, string installDirectory, string outputDirectory, out string? replacedPlugin)
    {
        var previous = GetInstallDirectory(root);
        var changed = SetPlugin(root, installDirectory, previous, out replacedPlugin);
        changed |= SetServer(root, installDirectory);
        changed |= SetPermissions(root, outputDirectory);
        return changed;
    }

    private static bool SetPlugin(JsonObject root, string installDirectory, string? previous, out string? replaced)
    {
        replaced = null;
        var plugins = GetOrCreateArray(root, "plugins", "plugins");
        var entry = Path.Combine(installDirectory, PluginFolderName);
        var present = IndexOfPath(plugins, entry) >= 0;

        // The entry of an earlier install folder: replaced (or dropped if the new one is there already).
        if (previous != null && !PathUtil.SamePath(previous, installDirectory))
        {
            var oldIndex = IndexOfPath(plugins, Path.Combine(previous, PluginFolderName));
            if (oldIndex >= 0)
            {
                replaced = GetString(plugins[oldIndex]);
                if (present)
                    plugins.RemoveAt(oldIndex);
                else
                    plugins[oldIndex] = entry;
                return true;
            }
        }

        if (present)
            return false;

        plugins.Add(entry);
        return true;
    }

    // type and command are what makes the bridge work: always set. codemode is a user's choice: set only if missing.
    private static bool SetServer(JsonObject root, string installDirectory)
    {
        var mcp = GetOrCreateObject(root, "mcp", "mcp");
        var servers = GetOrCreateObject(mcp, "servers", "mcp.servers");
        var server = GetOrCreateObject(servers, ServerName, $"mcp.servers.{ServerName}");
        var changed = false;

        if (GetString(server["type"]) != "local")
        {
            server["type"] = "local";
            changed = true;
        }

        var exe = Path.Combine(installDirectory, ExeFileName);
        var commandOk = server["command"] is JsonArray command
            && command.Count == 2
            && GetString(command[0]) is { } existingExe
            && PathUtil.SamePath(existingExe, exe)
            && GetString(command[1]) == "mcp";
        if (!commandOk)
        {
            server["command"] = new JsonArray(exe, "mcp");
            changed = true;
        }

        // With Code Mode on, vsab_create_file is reachable only through "execute", and many models miss it.
        if (server["codemode"] == null)
        {
            server["codemode"] = false;
            changed = true;
        }

        return changed;
    }

    // Both permissions are a user's choice: set only if missing.
    private static bool SetPermissions(JsonObject root, string outputDirectory)
    {
        var permission = GetOrCreateObject(root, "permission", "permission");
        var changed = false;

        // OpenCode matches external_directory patterns with forward slashes.
        var pattern = outputDirectory.Replace('\\', '/').TrimEnd('/') + "/*";
        var external = permission["external_directory"];
        if (external == null)
        {
            permission["external_directory"] = new JsonObject { [pattern] = "allow" };
            changed = true;
        }
        else if (external is JsonObject patterns)
        {
            if (!patterns.Any(p => PathUtil.SamePath(p.Key, pattern)))
            {
                patterns[pattern] = "allow";
                changed = true;
            }
        }
        else
        {
            throw new InvalidDataException(
                $"'permission.external_directory' is not an object; allow \"{pattern}\" in it by hand");
        }

        if (permission[CreateFilePermission] == null)
        {
            permission[CreateFilePermission] = "ask";
            changed = true;
        }

        return changed;
    }

    private static int IndexOfPath(JsonArray list, string path)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (GetString(list[i]) is { } item && PathUtil.SamePath(item, path))
                return i;
        }
        return -1;
    }
}