using System.Runtime.CompilerServices;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// The guard for text about to be stored: what a database column refuses, refused here first with
/// a message that never quotes the text.
/// </summary>
public static class StorableTextExtensions
{
    /// <summary>
    /// Returns <paramref name="text"/> when a text column can store it: at most
    /// <paramref name="maxLength"/> UTF-16 code units, no U+0000 (PostgreSQL text and JSON columns
    /// refuse it outright), and no lone surrogate (it has no UTF-8 form, so a strict encoder refuses
    /// it and a lenient one would store U+FFFD instead).
    /// </summary>
    /// <remarks>
    /// The messages name the lengths or the kind of character, never the text, which may be a secret.
    /// Pure and thread-safe.
    /// </remarks>
    /// <param name="text">The text to store.</param>
    /// <param name="maxLength">The most UTF-16 code units the column holds; at least 0.</param>
    /// <param name="parameterName">
    /// The parameter the exception names; defaults to the source expression of
    /// <paramref name="text"/>.
    /// </param>
    /// <returns><paramref name="text"/> itself.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// The text is longer than <paramref name="maxLength"/>, holds U+0000, or holds a lone surrogate.
    /// </exception>
    public static string RequireStorable(this string text, int maxLength, [CallerArgumentExpression(nameof(text))] string parameterName = "")
    {
        ArgumentNullException.ThrowIfNull(text, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        if (text.Length > maxLength)
        {
            throw new ArgumentException($"At most {maxLength} characters are stored; this holds {text.Length}.", parameterName);
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("U+0000 cannot be stored.", parameterName);
        }

        if (HasLoneSurrogate(text))
        {
            throw new ArgumentException("A lone surrogate cannot be stored.", parameterName);
        }

        return text;
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (int index = text.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF'); index >= 0 && index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(text[index]) || index + 1 == text.Length || !char.IsLowSurrogate(text[index + 1]))
            {
                return true;
            }

            index++;
        }

        return false;
    }
}
