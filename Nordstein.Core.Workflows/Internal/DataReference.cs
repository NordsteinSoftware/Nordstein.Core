using System.Text.RegularExpressions;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// What a data reference reads: the trigger's output, a step's output, or, inside a loop body, the current item or its index.
/// </summary>
internal enum ReferenceRoot
{
    Trigger,
    Step,
    Item,
    Index,
}

/// <summary>
/// One data reference: its <see cref="Root"/>, the step's node id for <see cref="ReferenceRoot.Step"/>, and a path into it.
/// </summary>
/// <param name="Root">What it reads.</param>
/// <param name="NodeId">The referenced step's node id; null for every other root.</param>
/// <param name="Path">Property names or array indexes; empty for the whole value.</param>
/// <param name="Text">The reference as written, for messages.</param>
internal sealed record DataReference(ReferenceRoot Root, string? NodeId, IReadOnlyList<string> Path, string Text);

/// <summary>
/// A configuration string's references: <see cref="IsWhole"/> when the string is exactly one reference,
/// <see cref="IsMalformed"/> when a <c>{{</c> or <c>}}</c> is left that is not part of a reference.
/// </summary>
internal sealed record TemplateParse(IReadOnlyList<DataReference> References, bool IsWhole, bool IsMalformed);

/// <summary>
/// Finds the data references in a configuration string. Stateless and thread-safe.
/// </summary>
internal sealed class ReferenceParser
{
    private readonly Regex reference = new(
        @"\{\{\s*(?:(?<trigger>trigger)|(?<item>item)|(?<index>index)|steps\.(?<node>[A-Za-z][A-Za-z0-9_-]{0,63})\.output)(?<path>(?:\.[A-Za-z0-9_-]+)*)\s*\}\}",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public TemplateParse Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<DataReference> references = [];
        MatchCollection matches = reference.Matches(text);
        foreach (Match match in matches)
        {
            references.Add(Reference(match));
        }

        string rest = reference.Replace(text, string.Empty);
        bool malformed = rest.Contains("{{", StringComparison.Ordinal) || rest.Contains("}}", StringComparison.Ordinal);
        bool whole = matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == text.Length;
        return new TemplateParse(references, whole, malformed);
    }

    /// <summary>
    /// Replaces every reference in <paramref name="text"/> with <paramref name="render"/>'s text for it.
    /// </summary>
    public string Render(string text, Func<DataReference, string> render)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(render);
        return reference.Replace(text, match => render(Reference(match)));
    }

    private static DataReference Reference(Match match)
    {
        string path = match.Groups["path"].Value;
        string[] segments = path.Length == 0 ? [] : path[1..].Split('.');
        ReferenceRoot root = match.Groups["trigger"].Success ? ReferenceRoot.Trigger
            : match.Groups["item"].Success ? ReferenceRoot.Item
            : match.Groups["index"].Success ? ReferenceRoot.Index
            : ReferenceRoot.Step;
        return new DataReference(root, root == ReferenceRoot.Step ? match.Groups["node"].Value : null, segments, match.Value);
    }
}
