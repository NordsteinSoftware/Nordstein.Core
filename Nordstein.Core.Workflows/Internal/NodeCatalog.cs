using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// A node catalogue indexed by <see cref="NodeTypeReference"/>: the type a graph's node names, and what is
/// wrong when it names none.
/// </summary>
internal sealed class NodeCatalog
{
    private readonly Dictionary<NodeTypeReference, INodeType> types;
    private readonly Dictionary<string, List<int>> majors;

    private NodeCatalog(Dictionary<NodeTypeReference, INodeType> types, Dictionary<string, List<int>> majors)
    {
        this.types = types;
        this.majors = majors;
    }

    /// <exception cref="ArgumentNullException"><paramref name="nodeTypes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeTypes"/> holds a null item, a type whose name or major is invalid, a read-only type with side effects or a suspending one, or two types of one name and major.</exception>
    public static NodeCatalog Of(IReadOnlyCollection<INodeType> nodeTypes)
    {
        ArgumentNullException.ThrowIfNull(nodeTypes);
        Dictionary<NodeTypeReference, INodeType> types = [];
        Dictionary<string, List<int>> majors = new(StringComparer.Ordinal);
        foreach (INodeType type in nodeTypes)
        {
            if (type is null)
            {
                throw new ArgumentException("The node catalogue holds no null type.", nameof(nodeTypes));
            }

            NodeTypeReference reference;
            try
            {
                reference = type.Reference();
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"The node catalogue's type '{type.Type}' has an invalid name or major: {exception.Message}", nameof(nodeTypes), exception);
            }

            if (type.ReadOnly && type.HasSideEffects)
            {
                throw new ArgumentException($"The node catalogue's type '{reference}' is read-only but has side effects.", nameof(nodeTypes));
            }

            if (type.ReadOnly && type is ISuspendingNodeType)
            {
                throw new ArgumentException($"The node catalogue's type '{reference}' is read-only but suspends.", nameof(nodeTypes));
            }

            if (!types.TryAdd(reference, type))
            {
                throw new ArgumentException($"The node catalogue names type '{reference}' twice.", nameof(nodeTypes));
            }

            if (!majors.TryGetValue(reference.Name, out List<int>? known))
            {
                known = [];
                majors[reference.Name] = known;
            }

            known.Add(reference.Major);
        }

        foreach (List<int> known in majors.Values)
        {
            known.Sort();
        }

        return new NodeCatalog(types, majors);
    }

    /// <summary>The type <paramref name="nodeType"/> (a <see cref="NodeDefinition.Type"/>) names, when the catalogue holds it.</summary>
    public bool TryGet(string nodeType, [NotNullWhen(true)] out INodeType? type)
    {
        if (NodeTypeReference.TryParse(nodeType, out NodeTypeReference? reference) && reference is not null && types.TryGetValue(reference, out INodeType? found))
        {
            type = found;
            return true;
        }

        type = null;
        return false;
    }

    /// <summary>Why <paramref name="node"/>'s type names nothing in the catalogue, or null when it names a type.</summary>
    public string? Problem(NodeDefinition node)
    {
        if (!NodeTypeReference.TryParse(node.Type, out NodeTypeReference? reference) || reference is null)
        {
            return $"Node '{node.Id}' has the type '{node.Type}', which is not of the form name@major.";
        }

        if (types.ContainsKey(reference))
        {
            return null;
        }

        return majors.TryGetValue(reference.Name, out List<int>? known)
            ? $"Node '{node.Id}' has the unknown type '{node.Type}'; this catalogue offers {string.Join(", ", known.Select(major => $"'{reference.Name}@{major.ToString(CultureInfo.InvariantCulture)}'"))}."
            : $"Node '{node.Id}' has the unknown type '{node.Type}'.";
    }

    /// <summary>The newest major of <paramref name="type"/>'s name when it is newer than <paramref name="type"/>'s own; null when <paramref name="type"/> is current.</summary>
    public NodeTypeReference? Successor(INodeType type)
    {
        int newest = majors[type.Type][^1];
        return newest > type.Major ? new NodeTypeReference(type.Type, newest) : null;
    }
}
