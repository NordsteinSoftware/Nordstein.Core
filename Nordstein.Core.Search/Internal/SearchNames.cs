namespace Nordstein.Core.Search.Internal;

/// <summary>
/// The name forms of <see cref="SearchIndexDefinition"/>. A provider interpolates both names into
/// statements where a parameter cannot stand, so the forms admit nothing but plain identifiers.
/// </summary>
internal static class SearchNames
{
    private const int MaxIndexNameLength = 40;
    private const int MaxTextSearchConfigLength = 63;

    public static bool IsIndexName(string value)
        => value.Length is > 0 and <= MaxIndexNameLength
            && IsLowerLetter(value[0])
            && value.All(IsIdentifierCharacter);

    public static bool IsTextSearchConfig(string value)
        => value.Length is > 0 and <= MaxTextSearchConfigLength
            && (IsLowerLetter(value[0]) || value[0] == '_')
            && value.All(IsIdentifierCharacter);

    private static bool IsLowerLetter(char character) => character is >= 'a' and <= 'z';

    private static bool IsIdentifierCharacter(char character)
        => IsLowerLetter(character) || character is (>= '0' and <= '9') or '_';
}
