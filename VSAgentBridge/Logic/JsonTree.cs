using System.Text.Json.Nodes;

namespace VSAgentBridge.Logic;

/// <summary>
/// Small helpers for the JSON trees of agent settings files (see <see cref="JsonFile"/>):
/// reading that tolerates an unexpected structure, and getting or creating nested objects.
/// </summary>
internal static class JsonTree
{
    /// <summary>
    /// Walks object properties by <paramref name="names"/>; returns null if a property is missing or
    /// something on the way is not an object (the indexer of JsonNode throws for a non-object).
    /// </summary>
    public static JsonNode? Find(JsonObject root, params string[] names)
    {
        JsonNode? node = root;
        foreach (var name in names)
        {
            if (node is not JsonObject obj)
                return null;
            node = obj[name];
        }
        return node;
    }

    /// <summary>The value of <paramref name="node"/> if it is a string, otherwise null.</summary>
    public static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// The object in property <paramref name="name"/> of <paramref name="parent"/>, created if the property
    /// is missing or null. Throws <see cref="InvalidDataException"/> naming <paramref name="pathForMessage"/>
    /// if the property holds something else; the tree is not changed then.
    /// </summary>
    public static JsonObject GetOrCreateObject(JsonObject parent, string name, string pathForMessage)
    {
        var node = parent[name];
        if (node == null)
        {
            var created = new JsonObject();
            parent[name] = created;
            return created;
        }

        return node as JsonObject ?? throw new InvalidDataException($"'{pathForMessage}' is not an object");
    }


    /// <summary>
    /// The list in property <paramref name="name"/> of <paramref name="parent"/>, created if the property
    /// is missing or null. Throws <see cref="InvalidDataException"/> naming <paramref name="pathForMessage"/>
    /// if the property holds something else; the tree is not changed then.
    /// </summary>
    public static JsonArray GetOrCreateArray(JsonObject parent, string name, string pathForMessage)
    {
        var node = parent[name];
        if (node == null)
        {
            var created = new JsonArray();
            parent[name] = created;
            return created;
        }

        return node as JsonArray ?? throw new InvalidDataException($"'{pathForMessage}' is not a list");
    }
}