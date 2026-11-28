using System.Globalization;
using System.Text;

namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// pgvector's text input form, <c>[v1,v2,…]</c>. Vectors travel as text cast to <c>vector</c> or
/// <c>halfvec</c>, so this package needs no driver plugin for the binary form.
/// </summary>
internal static class VectorLiteral
{
    /// <summary>
    /// Formats <paramref name="vector"/> after checking it against the index.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The vector has another length than <paramref name="dimension"/>, or a component is NaN or
    /// infinite.
    /// </exception>
    public static string ToVectorLiteral(this ReadOnlyMemory<float> vector, int dimension, string parameterName)
    {
        ReadOnlySpan<float> values = vector.Span;
        if (values.Length != dimension)
        {
            throw new ArgumentException(
                $"The vector has {values.Length} dimensions; the index holds vectors of {dimension}.", parameterName);
        }

        var text = new StringBuilder((values.Length * 12) + 2);
        text.Append('[');
        for (int index = 0; index < values.Length; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                throw new ArgumentException($"Vector component {index} is not finite.", parameterName);
            }

            if (index > 0)
            {
                text.Append(',');
            }

            text.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }

        return text.Append(']').ToString();
    }
}
