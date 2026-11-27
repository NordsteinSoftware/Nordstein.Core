using System.Globalization;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Workflows;

/// <summary>
/// How a graph names a node type: a type's <see cref="INodeType.Type"/> and its <see cref="INodeType.Major"/>
/// as the text <c>name@major</c> (<c>condition@1</c>). A saved graph pins the major it was written for, so a
/// product keeps running it after shipping a newer major of the same name.
/// </summary>
/// <remarks>
/// A name is a letter followed by up to 63 letters, digits, <c>.</c>, <c>-</c> or <c>_</c>; a major is a decimal
/// integer from 1 up, without a sign or leading zeros. Immutable and thread-safe; value equality.
/// </remarks>
public sealed partial record NodeTypeReference
{
    /// <summary>The longest name a type may have.</summary>
    public const int MaxNameLength = 64;

    private static readonly Regex Form = Pattern();

    /// <summary>
    /// Creates a reference.
    /// </summary>
    /// <param name="name">The type's name.</param>
    /// <param name="major">The type's major version, 1 or more.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a letter followed by up to 63 letters, digits, <c>.</c>, <c>-</c> or <c>_</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="major"/> is less than 1.</exception>
    public NodeTypeReference(string name, int major)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 1);
        if (!Form.IsMatch(name + "@1"))
        {
            throw new ArgumentException($"A node type's name is a letter followed by up to {MaxNameLength - 1} letters, digits, '.', '-' or '_'; '{name}' is not.", nameof(name));
        }

        Name = name;
        Major = major;
    }

    /// <summary>The type's name (<see cref="INodeType.Type"/>).</summary>
    public string Name { get; }

    /// <summary>The type's major version (<see cref="INodeType.Major"/>).</summary>
    public int Major { get; }

    /// <summary>
    /// Parses <c>name@major</c>.
    /// </summary>
    /// <param name="text">The text, such as a <see cref="NodeDefinition.Type"/>.</param>
    /// <param name="reference">The reference, or null when <paramref name="text"/> is not of the form.</param>
    /// <returns>Whether <paramref name="text"/> is of the form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static bool TryParse(string text, out NodeTypeReference? reference)
    {
        ArgumentNullException.ThrowIfNull(text);
        Match match = Form.Match(text);
        if (!match.Success || !int.TryParse(match.Groups["major"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int major))
        {
            reference = null;
            return false;
        }

        reference = new NodeTypeReference(match.Groups["name"].Value, major);
        return true;
    }

    /// <summary>
    /// Parses <c>name@major</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="text"/> is not of the form.</exception>
    public static NodeTypeReference Parse(string text) =>
        TryParse(text, out NodeTypeReference? reference) && reference is not null
            ? reference
            : throw new FormatException($"'{text}' is not a node type reference of the form name@major.");

    /// <summary>
    /// Whether <paramref name="text"/> is a reference of the form with the name <paramref name="name"/>, at any major.
    /// </summary>
    /// <param name="text">The text, such as a <see cref="NodeDefinition.Type"/>.</param>
    /// <param name="name">The type's name (<see cref="INodeType.Type"/>), compared ordinally.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="name"/> is null.</exception>
    public static bool Names(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TryParse(text, out NodeTypeReference? reference) && reference is not null && string.Equals(reference.Name, name, StringComparison.Ordinal);
    }

    /// <summary>The text <c>name@major</c>.</summary>
    public override string ToString() => Name + "@" + Major.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex("^(?<name>[A-Za-z][A-Za-z0-9._-]{0,63})@(?<major>[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}

/// <summary>
/// Helpers on <see cref="INodeType"/>.
/// </summary>
public static class NodeTypeExtensions
{
    /// <summary>
    /// The reference a graph names <paramref name="type"/> by: its <see cref="INodeType.Type"/> at its
    /// <see cref="INodeType.Major"/>.
    /// </summary>
    /// <param name="type">The node type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    /// <exception cref="ArgumentException"><see cref="INodeType.Type"/> is not a valid name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="INodeType.Major"/> is less than 1.</exception>
    public static NodeTypeReference Reference(this INodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new NodeTypeReference(type.Type, type.Major);
    }

    /// <summary>
    /// Whether <paramref name="node"/>'s type has the name <paramref name="name"/>, at any major
    /// (<see cref="NodeTypeReference.Names"/>).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="name">The type's name (<see cref="INodeType.Type"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="name"/> is null.</exception>
    public static bool HasName(this NodeDefinition node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        return NodeTypeReference.Names(node.Type, name);
    }
}
