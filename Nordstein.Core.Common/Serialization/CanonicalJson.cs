using System.Text.Json.Nodes;

namespace Nordstein.Core.Common.Serialization;

/// <summary>
/// One spelling per JSON value, so a hash over it is stable however a producer ordered or spaced
/// the same document.
/// </summary>
/// <remarks>
/// <para>
/// The canonical form sorts the properties of every object ordinally (<see cref="StringComparer.Ordinal"/>)
/// at every depth, keeps array order, writes no whitespace and escapes strings with
/// <c>System.Text.Json</c>'s default encoder (non-ASCII and HTML-sensitive characters as
/// <c>\uXXXX</c>). A number parsed from text keeps its spelling (<c>1.50</c> stays <c>1.50</c>), so
/// two spellings of one number are two canonical forms; it is a canonical form of the document as
/// written, not of its numeric values (not RFC 8785).
/// </para>
/// <para>
/// Pure and thread-safe for inputs no other thread mutates: the input node is read, never changed.
/// </para>
/// </remarks>
public static class CanonicalJson
{
    /// <summary>
    /// Returns the canonical JSON text of <paramref name="node"/>.
    /// </summary>
    /// <param name="node">Any JSON node, attached to a parent or not; <see langword="null"/> is the JSON literal <c>null</c>.</param>
    /// <returns>Compact JSON text with every object's properties sorted ordinally.</returns>
    public static string Serialize(JsonNode? node) => Sorted(node)?.ToJsonString() ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject members => new JsonObject(members
            .OrderBy(member => member.Key, StringComparer.Ordinal)
            .Select(member => KeyValuePair.Create(member.Key, Sorted(member.Value)))),
        JsonArray items => new JsonArray([.. items.Select(Sorted)]),
        _ => node?.DeepClone(),
    };
}
