using System.Collections.Frozen;

namespace Nordstein.Core.Search.Internal;

/// <summary>
/// The facet rules shared by entries and filters, and the defensive copy both take.
/// </summary>
internal static class Facets
{
    public static FrozenSet<string> Copy(IEnumerable<string> facets, int maxCount, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(facets, parameterName);
        FrozenSet<string> copy = facets.ToFrozenSet(StringComparer.Ordinal);
        if (copy.Count > maxCount)
        {
            throw new ArgumentException($"At most {maxCount} facets are allowed; got {copy.Count}.", parameterName);
        }

        foreach (string facet in copy)
        {
            if (!IsFacet(facet))
            {
                throw new ArgumentException(
                    $"A facet has 1 to {SearchEntry.MaxFacetLength} characters, none of them a control character or a lone surrogate.",
                    parameterName);
            }
        }

        return copy;
    }

    private static bool IsFacet(string? facet)
        => facet is { Length: > 0 and <= SearchEntry.MaxFacetLength }
            && !facet.Any(char.IsControl)
            && !HasLoneSurrogate(facet);

    private static bool HasLoneSurrogate(string facet)
    {
        for (int index = 0; index < facet.Length; index++)
        {
            if (char.IsHighSurrogate(facet[index]) && index + 1 < facet.Length && char.IsLowSurrogate(facet[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(facet[index]))
            {
                return true;
            }
        }

        return false;
    }
}
