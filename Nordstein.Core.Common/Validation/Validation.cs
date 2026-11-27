using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Common.Validation;

public static class Validation
{
    /// <summary>
    /// The "no error" result every check below returns on success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <b>one sanctioned null-suppression in the repository</b> (CLAUDE.md otherwise
    /// forbids <c>!</c> outright), and it exists to work around a BCL under-annotation that cannot
    /// be fixed from here.
    /// </para>
    /// <para>
    /// <see cref="ValidationResult.Success"/> is declared <c>ValidationResult?</c> and its value
    /// <b>is</b> <c>null</c> — that is how the framework represents success, and
    /// <see cref="Validator"/> detects it by comparing against <c>null</c>. Yet
    /// <see cref="IValidatableObject.Validate"/>, which every caller implements, is declared to
    /// return <c>IEnumerable&lt;ValidationResult&gt;</c> with a <b>non-nullable</b> element type. So
    /// the framework requires us to yield a value it itself defines as null, through a signature we
    /// cannot change. Returning any genuinely non-null sentinel instead would be read by
    /// <see cref="Validator"/> as a validation <i>failure</i>, breaking every entity.
    /// </para>
    /// <para>
    /// Concentrating the suppression here keeps it to a single reviewed line instead of one per
    /// check. Do not copy the pattern elsewhere; if you need "success" in a new check, return this.
    /// </para>
    /// </remarks>
    // ReSharper disable once NullableWarningSuppressionIsUsed -- see the remarks above.
    private static ValidationResult Success => ValidationResult.Success!;

    /// <summary>
    /// Adapts a nullable <see cref="ValidationResult"/> (where <c>null</c> represents success, matching
    /// <see cref="ValidationResult.Success"/>) to a non-null <see cref="IEnumerable{ValidationResult}"/>
    /// suitable for <see cref="IValidatableObject.Validate"/> implementations.
    /// </summary>
    public static IEnumerable<ValidationResult> AsEnumerable(this ValidationResult? result)
    {
        if (result is not null)
        {
            yield return result;
        }
    }

    /// <summary>
    /// Validates that <paramref name="value"/> is not <c>null</c>.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is not <c>null</c>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotNull(object? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value is null
            ? new ValidationResult($"{memberName} cannot be null", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is <c>null</c>.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is <c>null</c>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult Null(object? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value is not null
            ? new ValidationResult($"{memberName} must be null", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is neither <c>null</c> nor whitespace.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> contains at least one
    /// non-whitespace character; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => string.IsNullOrWhiteSpace(value)
            ? new ValidationResult($"{memberName} cannot be empty", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is neither <c>null</c> nor an empty string.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is a non-null,
    /// non-empty string (whitespace-only strings pass); otherwise a <see cref="ValidationResult"/>
    /// describing the failure.
    /// </returns>
    public static ValidationResult NotNullOrEmpty(string? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => string.IsNullOrEmpty(value)
            ? new ValidationResult($"{memberName} cannot be null or empty", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is not the default value for its type.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is not equal to
    /// <c>default(T)</c>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotDefault<T>(T value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => EqualityComparer<T>.Default.Equals(value, default)
            ? new ValidationResult($"{memberName} cannot be default", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is in the past relative to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is less than or equal to
    /// <see cref="DateTimeOffset.UtcNow"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult InPast(DateTimeOffset value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value > DateTimeOffset.UtcNow
            ? new ValidationResult($"{memberName} must be in the past", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is in the future relative to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is strictly greater than
    /// <see cref="DateTimeOffset.UtcNow"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult InFuture(DateTimeOffset value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value <= DateTimeOffset.UtcNow
            ? new ValidationResult($"{memberName} must be in the future", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is in the past relative to <paramref name="now"/>, the
    /// caller's clock (<see cref="Time.IClock.UtcNow"/>). Prefer this over the system-clock overload
    /// wherever a clock is injected: a host whose clock runs ahead of or behind the machine's then
    /// judges its own timestamps consistently.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="now">The instant that counts as "now".</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is less than or equal to
    /// <paramref name="now"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult InPast(DateTimeOffset value, DateTimeOffset now, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value > now
            ? new ValidationResult($"{memberName} must be in the past", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is in the future relative to <paramref name="now"/>, the
    /// caller's clock (<see cref="Time.IClock.UtcNow"/>). Prefer this over the system-clock overload
    /// wherever a clock is injected.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="now">The instant that counts as "now".</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is strictly greater than
    /// <paramref name="now"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult InFuture(DateTimeOffset value, DateTimeOffset now, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value <= now
            ? new ValidationResult($"{memberName} must be in the future", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is not before <paramref name="minValue"/>.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="minValue">The earliest acceptable value (inclusive).</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is greater than or equal
    /// to <paramref name="minValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotBefore(DateTimeOffset value, DateTimeOffset minValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value < minValue
            ? new ValidationResult($"{memberName} cannot be before {minValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is not after <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="value">The date/time to check.</param>
    /// <param name="maxValue">The latest acceptable value (inclusive).</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is less than or equal
    /// to <paramref name="maxValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotAfter(DateTimeOffset value, DateTimeOffset maxValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value > maxValue
            ? new ValidationResult($"{memberName} cannot be after {maxValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is not negative (i.e., zero or greater).
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is greater than or equal
    /// to zero; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotNegative(decimal value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value < 0
            ? new ValidationResult($"{memberName} cannot be negative", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is strictly positive (greater than zero).
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is greater than zero;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult Positive(decimal value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value <= 0
            ? new ValidationResult($"{memberName} must be positive", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is strictly less than <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="maxValue">The exclusive upper bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is strictly less than
    /// <paramref name="maxValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult LessThan(decimal value, decimal maxValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value >= maxValue
            ? new ValidationResult($"{memberName} must be less than {maxValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is less than or equal to <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="maxValue">The inclusive upper bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is less than or equal
    /// to <paramref name="maxValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult LessThanOrEqual(decimal value, decimal maxValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value > maxValue
            ? new ValidationResult($"{memberName} must be less than or equal to {maxValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is strictly greater than <paramref name="minValue"/>.
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="minValue">The exclusive lower bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is strictly greater than
    /// <paramref name="minValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult GreaterThan(decimal value, decimal minValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value <= minValue
            ? new ValidationResult($"{memberName} must be greater than {minValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is greater than or equal to <paramref name="minValue"/>.
    /// </summary>
    /// <param name="value">The decimal value to check.</param>
    /// <param name="minValue">The inclusive lower bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is greater than or equal
    /// to <paramref name="minValue"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult GreaterThanOrEqual(decimal value, decimal minValue, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value < minValue
            ? new ValidationResult($"{memberName} must be greater than or equal to {minValue}", [memberName])
            : Success;

    /// <summary>
    /// Validates that the collection <paramref name="value"/> contains exactly <paramref name="count"/> elements.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="count">The exact number of elements expected.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <c>value.Count == count</c>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult HasCount<T>(IReadOnlyCollection<T> value, int count, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value.Count != count
            ? new ValidationResult($"{memberName} must have {count} items", [memberName])
            : Success;

    /// <summary>
    /// Validates that the string <paramref name="value"/> does not exceed <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="value">The string to check. <c>null</c> is treated as length zero.</param>
    /// <param name="maxLength">The maximum allowed length (inclusive).</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the string length is at most <paramref name="maxLength"/>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult MaxLength(string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => (value?.Length ?? 0) > maxLength
            ? new ValidationResult($"{memberName} cannot be longer than {maxLength} characters", [memberName])
            : Success;

    /// <summary>
    /// Validates that the string <paramref name="value"/> is at least <paramref name="minLength"/> characters long.
    /// </summary>
    /// <param name="value">The string to check. <c>null</c> is treated as length zero.</param>
    /// <param name="minLength">The minimum required length (inclusive).</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the string length is at least <paramref name="minLength"/>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult MinLength(string? value, int minLength, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => (value?.Length ?? 0) < minLength
            ? new ValidationResult($"{memberName} cannot be shorter than {minLength} characters", [memberName])
            : Success;

    /// <summary>
    /// Validates that the string <paramref name="value"/> is exactly <paramref name="length"/> characters long.
    /// </summary>
    /// <param name="value">The string to check. <c>null</c> is treated as length zero.</param>
    /// <param name="length">The exact required length.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the string length equals <paramref name="length"/>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult ExactLength(string? value, int length, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => (value?.Length ?? 0) != length
            ? new ValidationResult($"{memberName} must be exactly {length} characters", [memberName])
            : Success;

    /// <summary>
    /// Validates that the string <paramref name="value"/> is not empty (has length of at least one character).
    /// </summary>
    /// <param name="value">The string to check. <c>null</c> is treated as length zero and fails.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the string has at least one character;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotEmpty(string? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => MinLength(value, 1, memberName);

    /// <summary>
    /// Validates that the nullable value <paramref name="value"/> is neither <c>null</c> nor <c>default(T)</c>: both
    /// mean "nothing named" for an identifier such as a <see cref="Guid"/>.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> has a non-default value; otherwise a
    /// <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotNullOrDefault<T>(T? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        where T : struct
        => value is not { } present || EqualityComparer<T>.Default.Equals(present, default)
            ? new ValidationResult($"{memberName} cannot be null or default", [memberName])
            : Success;

    /// <summary>
    /// Validates that the collection <paramref name="value"/> is empty.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the collection has no element; otherwise a
    /// <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public static ValidationResult Empty<T>(IReadOnlyCollection<T> value, [CallerArgumentExpression(nameof(value))] string memberName = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Count == 0
            ? Success
            : new ValidationResult($"{memberName} must be empty", [memberName]);
    }

    /// <summary>
    /// Validates that no element of <paramref name="value"/> equals <c>default(T)</c> (<c>null</c> for a reference type,
    /// <see cref="Guid.Empty"/> for a <see cref="Guid"/>). Enumerates <paramref name="value"/> once, up to the first default.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="value">The elements to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when no element is the default; otherwise a
    /// <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public static ValidationResult NoneDefault<T>(IEnumerable<T> value, [CallerArgumentExpression(nameof(value))] string memberName = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Any(element => EqualityComparer<T>.Default.Equals(element, default))
            ? new ValidationResult($"{memberName} cannot contain a default value", [memberName])
            : Success;
    }

    /// <summary>
    /// Validates that the collection <paramref name="value"/> is not empty.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the collection contains at least one element;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult NotEmpty<T>(IReadOnlyCollection<T> value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value.Count == 0
            ? new ValidationResult($"{memberName} cannot be empty", [memberName])
            : Success;

    /// <summary>
    /// Validates that the string <paramref name="value"/> is non-null and matches the regular expression
    /// <paramref name="pattern"/>.
    /// </summary>
    /// <param name="value">The string to check. <c>null</c> always fails.</param>
    /// <param name="pattern">A regular expression pattern that <paramref name="value"/> must fully satisfy.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is non-null and contains
    /// a match for <paramref name="pattern"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult Matches(string? value, string pattern, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value is null || !Regex.IsMatch(value, pattern)
            ? new ValidationResult($"{memberName} does not match the required pattern", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is a syntactically valid absolute URI with a non-empty host.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> parses as an absolute URI
    /// with a non-empty host; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult ValidUri(string? value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => !Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            ? new ValidationResult($"{memberName} must be a valid absolute URI", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is a defined constant of the enum type <typeparamref name="TEnum"/>.
    /// </summary>
    /// <typeparam name="TEnum">The enum type. Must be a struct and an <see cref="Enum"/>.</typeparam>
    /// <param name="value">The enum value to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is a named constant of
    /// <typeparamref name="TEnum"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    /// <remarks>
    /// Rejects numeric values that are not defined as named constants, including out-of-range integers
    /// cast to the enum type.
    /// </remarks>
    public static ValidationResult Defined<TEnum>(TEnum value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        where TEnum : struct, Enum
        => !Enum.IsDefined(typeof(TEnum), value)
            ? new ValidationResult($"{memberName} has an undefined value {value}", [memberName])
            : Success;

    /// <summary>
    /// Validates that <paramref name="value"/> is valid JSON.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message and <see cref="ValidationResult.MemberNames"/>.
    /// Defaults to the source expression of <paramref name="value"/> via
    /// <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> parses as valid JSON;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult Json(string value, [CallerArgumentExpression(nameof(value))] string memberName = "")
    {
        try
        {
            JsonDocument.Parse(value);
            return Success;
        }
        catch
        {
            return new ValidationResult($"{memberName} is not valid JSON", [memberName]);
        }
    }

    /// <summary>
    /// Validates that the integer <paramref name="value"/> is within the inclusive range
    /// [<paramref name="greateOrEqual"/>, <paramref name="lessOrEqual"/>].
    /// </summary>
    /// <param name="value">The integer value to check.</param>
    /// <param name="greateOrEqual">The inclusive lower bound.</param>
    /// <param name="lessOrEqual">The inclusive upper bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message. Defaults to the source expression of
    /// <paramref name="value"/> via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is between
    /// <paramref name="greateOrEqual"/> and <paramref name="lessOrEqual"/> inclusive;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult InRange(int value, int greateOrEqual, int lessOrEqual, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => value < greateOrEqual || value > lessOrEqual
            ? new ValidationResult($"{memberName} must be between {greateOrEqual} and {lessOrEqual}", [memberName])
            : Success;

    /// <summary>
    /// Validates that the integer <paramref name="variable"/> is strictly greater than <paramref name="greaterThan"/>.
    /// </summary>
    /// <param name="variable">The integer value to check.</param>
    /// <param name="greaterThan">The exclusive lower bound.</param>
    /// <param name="memberName">
    /// The name used in the failure message. Defaults to the source expression of
    /// <paramref name="variable"/> via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="variable"/> is strictly greater
    /// than <paramref name="greaterThan"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult GreaterThan(int variable, int greaterThan, [CallerArgumentExpression(nameof(variable))] string memberName = "")
        => variable <= greaterThan
            ? new ValidationResult($"{memberName} must be greater than {greaterThan}", [memberName])
            : Success;

    /// <summary>
    /// Validates that the <see cref="TimeSpan"/> <paramref name="variable"/> is strictly positive
    /// (greater than <see cref="TimeSpan.Zero"/>).
    /// </summary>
    /// <param name="variable">The time span to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message. Defaults to the source expression of
    /// <paramref name="variable"/> via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="variable"/> is greater than
    /// <see cref="TimeSpan.Zero"/>; otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult Positive(TimeSpan variable, [CallerArgumentExpression(nameof(variable))] string memberName = "")
        => variable <= TimeSpan.Zero
            ? new ValidationResult($"{memberName} must be positive", [memberName])
            : Success;

    /// <summary>
    /// Validates that the boolean <paramref name="value"/> is <c>true</c>.
    /// </summary>
    /// <param name="value">The boolean to check.</param>
    /// <param name="memberName">
    /// The name used in the failure message. Defaults to the source expression of
    /// <paramref name="value"/> via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when <paramref name="value"/> is <c>true</c>;
    /// otherwise a <see cref="ValidationResult"/> describing the failure.
    /// </returns>
    public static ValidationResult True(bool value, [CallerArgumentExpression(nameof(value))] string memberName = "")
        => !value
            ? new ValidationResult($"{memberName} must be true", [memberName])
            : Success;

    /// <summary>
    /// Validates the nested object <paramref name="child"/> and yields its failures reported under the
    /// parent member <paramref name="memberName"/>, so a failure in a child of a composite options or
    /// entity type names the full path (<c>Parent:Child</c>) rather than the child member alone.
    /// </summary>
    /// <param name="child">
    /// The nested object. It is validated like <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?, bool)"/>
    /// with all properties, so its <see cref="IValidatableObject.Validate"/> and its
    /// <see cref="ValidationAttribute"/>s both run. <c>null</c> yields nothing: required-ness is a
    /// separate rule (<see cref="NotNull"/>).
    /// </param>
    /// <param name="memberName">
    /// The parent member that holds <paramref name="child"/>. Defaults to the source expression of
    /// <paramref name="child"/> via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    /// <returns>
    /// One result per failure of <paramref name="child"/>, in the order the child reported them, each
    /// with its <see cref="ValidationResult.ErrorMessage"/> prefixed by <c><paramref name="memberName"/>:</c>
    /// and each member name <c>inner</c> replaced by <c>"{memberName}:{inner}"</c>. A failure that names
    /// no member is reported with the single member name <paramref name="memberName"/>. The sequence is
    /// empty when <paramref name="child"/> is <c>null</c> or valid. Nesting composes: a child that
    /// itself calls <see cref="Nested"/> yields paths such as <c>Outer:Inner:Member</c>.
    /// </returns>
    /// <remarks>
    /// The message prefix mirrors the member-name prefix because <see cref="Validator.ValidateObject(object, ValidationContext, bool)"/>
    /// reports only the message of the first failure; without it the message would name the inner member
    /// alone. The result is evaluated eagerly when enumerated, not when called.
    /// </remarks>
    public static IEnumerable<ValidationResult> Nested(object? child, [CallerArgumentExpression(nameof(child))] string memberName = "")
    {
        if (child is null)
        {
            yield break;
        }

        var failures = new List<ValidationResult>();
        Validator.TryValidateObject(child, new ValidationContext(child), failures, validateAllProperties: true);
        foreach (ValidationResult failure in failures)
        {
            yield return new ValidationResult(
                $"{memberName}:{failure.ErrorMessage}",
                failure.MemberNames.Any()
                    ? [.. failure.MemberNames.Select(inner => $"{memberName}:{inner}")]
                    : [memberName]);
        }
    }
}
