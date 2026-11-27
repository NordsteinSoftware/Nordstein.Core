using System.Collections;
using System.Text;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// A string addressed in Unicode code points (<see cref="Rune"/>s) instead of UTF-16 units: an index,
/// a length and a slice count an astral character such as an emoji once, so offsets computed over
/// this view never land inside a surrogate pair.
/// </summary>
/// <remarks>
/// A code point is not a grapheme: a base letter and a combining mark are two code points, and a
/// slice may separate them. A lone surrogate in the input reads as one <see cref="Rune.ReplacementChar"/>,
/// so the view of ill-formed UTF-16 is well-formed. The view copies the text once at construction;
/// it is immutable and safe to share between threads.
/// </remarks>
public sealed class CodePointText : IReadOnlyList<Rune>
{
    private readonly Rune[] runes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodePointText"/> class over the code points of
    /// <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public CodePointText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        runes = [.. text.EnumerateRunes()];
    }

    /// <summary>
    /// Gets the number of code points.
    /// </summary>
    public int Length => runes.Length;

    /// <summary>
    /// Gets the number of code points; the same as <see cref="Length"/>.
    /// </summary>
    public int Count => runes.Length;

    /// <summary>
    /// Gets the code point at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">A code-point index, from 0 to <see cref="Length"/> − 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not below <see cref="Length"/>.</exception>
    public Rune this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, runes.Length);
            return runes[index];
        }
    }

    /// <summary>
    /// Returns the code points of the half-open range from <paramref name="start"/> to
    /// <paramref name="end"/> as a string.
    /// </summary>
    /// <param name="start">The first code point, from 0 to <see cref="Length"/>.</param>
    /// <param name="end">The code point after the last, from <paramref name="start"/> to <see cref="Length"/>.</param>
    /// <returns>The slice; empty when <paramref name="start"/> equals <paramref name="end"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> is negative or beyond <see cref="Length"/>, or <paramref name="end"/> is
    /// before <paramref name="start"/> or beyond <see cref="Length"/>.
    /// </exception>
    public string Slice(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, runes.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, runes.Length);
        var builder = new StringBuilder(end - start);
        for (int index = start; index < end; index++)
        {
            builder.Append(runes[index].ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns an enumerator over the code points in order.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Rune> GetEnumerator() => ((IEnumerable<Rune>)runes).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
