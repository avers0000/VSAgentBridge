using System.Xml.Linq;

namespace VSAgentBridge.Logic;

/// <summary>
/// Parser for the TFVC client's local workspace cache (VersionControl.config).
/// Works on the XML text only: reading the file is the caller's job.
/// </summary>
internal static class TfvcWorkspaceCache
{
    /// <summary>
    /// Returns the workspaces listed in the cache. Entries without a name, computer
    /// or any mapped path are skipped. Throws XmlException for malformed XML.
    /// </summary>
    public static IReadOnlyList<TfvcWorkspace> Parse(string xml)
    {
        var result = new List<TfvcWorkspace>();

        var root = XDocument.Parse(xml).Root;
        if (root == null || root.Name.LocalName != "VersionControlServer")
            return result;

        var servers = root.Element("Servers")?.Elements("ServerInfo") ?? Enumerable.Empty<XElement>();
        foreach (var server in servers)
        {
            var serverUri = (string?)server.Attribute("uri") ?? "";

            foreach (var ws in server.Elements("WorkspaceInfo"))
            {
                var name = (string?)ws.Attribute("name");
                var computer = (string?)ws.Attribute("computer");
                var mappedPaths = (ws.Element("MappedPaths")?.Elements("MappedPath") ?? Enumerable.Empty<XElement>())
                    .Select(m => (string?)m.Attribute("path"))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!)
                    .ToList();

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(computer) || mappedPaths.Count == 0)
                    continue;

                result.Add(new TfvcWorkspace(
                    serverUri,
                    name!,
                    (string?)ws.Attribute("ownerName") ?? "",
                    computer!,
                    IsTrue((string?)ws.Attribute("isLocalWorkspace")),
                    mappedPaths));
            }
        }

        return result;
    }

    // Local workspaces appeared in TFS 2012; a cache without the attribute can only describe server workspaces.
    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}