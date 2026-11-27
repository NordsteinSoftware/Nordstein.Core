using System.Globalization;

namespace Nordstein.Core.Common.Io;

/// <summary>
/// Thrown by <see cref="StreamTextExtensions.ReadAllTextAsync"/> the moment a text payload grows past
/// its character cap; the rest of the stream is not read.
/// </summary>
public sealed class TextLimitExceededException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextLimitExceededException"/> class.
    /// </summary>
    /// <param name="maxCharacters">The cap the text exceeded.</param>
    public TextLimitExceededException(int maxCharacters)
        : base(string.Create(CultureInfo.InvariantCulture, $"The text exceeds {maxCharacters} characters."))
    {
        MaxCharacters = maxCharacters;
    }

    /// <summary>
    /// Gets the cap the text exceeded.
    /// </summary>
    public int MaxCharacters { get; }
}
