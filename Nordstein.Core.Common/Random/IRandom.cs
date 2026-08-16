namespace Nordstein.Core.Common.Random;

/// <summary>
/// Deterministic pseudo-random values for <b>generating test and demo data</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never use this for anything security-relevant.</b> The registered implementation is
/// <c>SeededRandom</c> — a <see cref="System.Random"/> with a <b>fixed seed</b>, so its output is
/// identical on every run and in every process. That is exactly what test-data generators and the
/// demo seeder want (reproducible fixtures) and exactly what a credential must never be.
/// </para>
/// <para>
/// Secrets — API keys, invite and password-reset tokens, TOTP secrets, MFA backup codes, stream
/// tickets — use <see cref="System.Security.Cryptography.RandomNumberGenerator"/> directly at their
/// point of use. <c>SeededRandomIsNotUsedForSecretsTests</c> enforces the separation: it fails if
/// any production type outside the generator surface takes an <see cref="IRandom"/> dependency.
/// </para>
/// </remarks>
public interface IRandom
{
    /// <summary>
    /// Returns a random boolean value.
    /// </summary>
    /// <returns><c>true</c> or <c>false</c> with equal probability.</returns>
    bool Bool();

    /// <summary>
    /// Returns a random <see cref="System.Guid"/>.
    /// </summary>
    /// <returns>A pseudo-randomly generated GUID. Not cryptographically random.</returns>
    Guid Guid();

    /// <summary>
    /// Returns a random string.
    /// </summary>
    /// <returns>
    /// A non-null string of pseudo-random characters. Not guaranteed to be unique across calls;
    /// use <see cref="UniqueString"/> when uniqueness within the process lifetime is required.
    /// </returns>
    string String();

    /// <summary>
    /// Returns a string that is unique within the current process lifetime.
    /// </summary>
    /// <returns>
    /// A non-null string that has not been returned by any previous call to this method in the
    /// current process. Suitable for use as a stable, reproducible unique identifier in test fixtures.
    /// </returns>
    string UniqueString();

    /// <summary>
    /// Returns a syntactically valid email address.
    /// </summary>
    /// <returns>
    /// A string that conforms to the basic email address format (e.g. <c>user@example.com</c>).
    /// The address is not a real mailbox and will not receive mail.
    /// </returns>
    string Email();

    /// <summary>
    /// Returns a syntactically valid absolute URI.
    /// </summary>
    /// <returns>
    /// A <see cref="System.Uri"/> with an absolute path. The URI does not correspond to a real
    /// endpoint and cannot be used for actual network requests.
    /// </returns>
    Uri Uri();

    /// <summary>
    /// Returns a random <see cref="int"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="int.MinValue"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="int.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random integer in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    int Int(int? min = null, int? max = null);

    /// <summary>
    /// Returns a random <see cref="long"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="long.MinValue"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="long.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random long in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    long Long(long? min = null, long? max = null);

    /// <summary>
    /// Returns a random <see cref="double"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="double.MinValue"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="double.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random double in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    double Double(double? min = null, double? max = null);

    /// <summary>
    /// Returns a random <see cref="decimal"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="decimal.MinValue"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="decimal.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random decimal in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    decimal Decimal(decimal? min = null, decimal? max = null);

    /// <summary>
    /// Returns a random element from the provided collection.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="options">The collection of values to choose from. Must not be empty.</param>
    /// <returns>One element chosen pseudo-randomly from <paramref name="options"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> is empty.
    /// </exception>
    T Any<T>(IReadOnlyCollection<T> options);

    /// <summary>
    /// Returns a random defined value of the specified enum type.
    /// </summary>
    /// <typeparam name="T">The enum type. Must be a struct and an <see cref="System.Enum"/>.</typeparam>
    /// <returns>One of the values defined by <typeparamref name="T"/>, chosen pseudo-randomly.</returns>
    T Enum<T>() where T : struct, Enum;

    /// <summary>
    /// Returns a random <see cref="System.TimeSpan"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="System.TimeSpan.Zero"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="System.TimeSpan.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random <see cref="System.TimeSpan"/> in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    TimeSpan TimeSpan(TimeSpan? min = null, TimeSpan? max = null);

    /// <summary>
    /// Returns a random <see cref="System.DateTimeOffset"/> within the specified inclusive range.
    /// </summary>
    /// <param name="min">
    /// The inclusive lower bound. When <c>null</c>, <see cref="System.DateTimeOffset.MinValue"/> is used.
    /// </param>
    /// <param name="max">
    /// The inclusive upper bound. When <c>null</c>, <see cref="System.DateTimeOffset.MaxValue"/> is used.
    /// </param>
    /// <returns>A pseudo-random <see cref="System.DateTimeOffset"/> in [<paramref name="min"/>, <paramref name="max"/>].</returns>
    DateTimeOffset DateTimeOffset(DateTimeOffset? min = null, DateTimeOffset? max = null);
}
