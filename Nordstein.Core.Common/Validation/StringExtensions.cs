using System.Diagnostics.CodeAnalysis;

namespace Nordstein.Core.Common.Validation;

/// <summary>
/// Extension methods for <see cref="string"/> that expose common null and whitespace checks
/// with nullable flow annotations understood by the C# compiler.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is non-null and contains at least one
    /// non-whitespace character.
    /// </summary>
    /// <param name="value">The string to test.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="value"/> is not null and not all whitespace;
    /// <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// Annotated <c>[NotNullWhen(true)]</c>: the compiler treats the input as non-null in branches
    /// where this method returns <c>true</c>.
    /// </remarks>
    public static bool NotNullOrWhiteSpace([NotNullWhen(true)] this string? value)
        => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is <c>null</c> or contains only whitespace characters.
    /// </summary>
    /// <param name="value">The string to test.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="value"/> is null or all whitespace;
    /// <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// Annotated <c>[NotNullWhen(false)]</c>: the compiler treats the input as non-null in branches
    /// where this method returns <c>false</c>.
    /// </remarks>
    public static bool NullOrWhiteSpace([NotNullWhen(false)] this string? value)
        => !value.NotNullOrWhiteSpace();
}
