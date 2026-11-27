using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// Checks a reference's path and type against the referenced node's output schema. Stateless and thread-safe.
/// </summary>
internal sealed class ReferenceChecker
{
    private readonly SchemaReader schemas;

    public ReferenceChecker(SchemaReader schemas)
    {
        this.schemas = schemas;
    }

    /// <summary>
    /// Why <paramref name="site"/> does not fit <paramref name="outputSchema"/>, the output schema of the referenced node
    /// <paramref name="sourceId"/>; null when it fits. A null schema (the source's configuration did not pass) allows any path.
    /// </summary>
    public string? Problem(ReferenceSite site, JsonElement? outputSchema, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(site);
        DataReference reference = site.Reference;
        JsonElement? schema = outputSchema;
        foreach (string segment in reference.Path)
        {
            if (schema is not { } current)
            {
                break;
            }

            if (!schemas.TryChild(current, segment, out schema))
            {
                return $"references '{segment}' in {reference.Text}, which the output of '{sourceId}' does not have.";
            }
        }

        JsonTypes actual = schema is { } resolved ? schemas.TypesOf(resolved) : JsonTypes.None;
        return schemas.Fits(actual, site.Expected)
            ? null
            : site.InText
                ? $"puts {reference.Text}, which is {schemas.Describe(actual)}, into text; only a string, number or boolean fits."
                : $"expects {schemas.Describe(site.Expected)}, but {reference.Text} is {schemas.Describe(actual)}.";
    }
}
