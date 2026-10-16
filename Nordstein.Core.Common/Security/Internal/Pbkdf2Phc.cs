using System.Globalization;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Encodes and strictly parses the PHC string of a PBKDF2-HMAC-SHA256 hash,
/// <c>$pbkdf2-sha256$i=&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>, with salt and hash in
/// standard base64 without padding. Pure and stateless.
/// </summary>
/// <remarks>
/// Parsing accepts exactly one spelling per value — no sign, no leading zero, no whitespace, no
/// padding, no non-zero trailing bits — and refuses parameters outside the bounds below before any
/// derivation runs, because a stored hash is input: an unbounded iteration count or output length
/// would let one row burn minutes of CPU per check.
/// </remarks>
internal static class Pbkdf2Phc
{
    /// <summary>
    /// The largest accepted iteration count: about 16 times the current default, room for later
    /// defaults while one check stays within seconds.
    /// </summary>
    internal const int MaxIterations = 10_000_000;

    /// <summary>
    /// The smallest accepted salt length in bytes.
    /// </summary>
    internal const int MinSaltSize = 8;

    /// <summary>
    /// The largest accepted salt length in bytes.
    /// </summary>
    internal const int MaxSaltSize = 64;

    /// <summary>
    /// The smallest accepted output length in bytes; a shorter one could match a wrong password by chance.
    /// </summary>
    internal const int MinHashSize = 16;

    /// <summary>
    /// The largest accepted output length in bytes. PBKDF2 repeats every iteration for each 32-byte
    /// block of output, so the output length multiplies the cost of a check.
    /// </summary>
    internal const int MaxHashSize = 64;

    private const string Prefix = "$pbkdf2-sha256$i=";

    // The decimal digits of MaxIterations; any longer count is out of range and could overflow.
    private const int MaxIterationDigits = 8;

    /// <summary>
    /// Formats the PHC string for the given parameters.
    /// </summary>
    internal static string Format(int iterations, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> hash)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{iterations}${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}");

    /// <summary>
    /// Parses a PHC string produced by <see cref="Format"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The value is not exactly such a string, or a parameter is out of bounds. The message names the
    /// defect, never the value.
    /// </exception>
    internal static Pbkdf2PhcParts Parse(string encoded)
    {
        ReadOnlySpan<char> rest = encoded;
        if (!rest.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw Malformed("it does not start with \"$pbkdf2-sha256$i=\"");
        }

        rest = rest[Prefix.Length..];
        int end = rest.IndexOf('$');
        if (end < 0)
        {
            throw Malformed("the salt and hash segments are missing");
        }

        int iterations = ParseIterations(rest[..end]);

        rest = rest[(end + 1)..];
        end = rest.IndexOf('$');
        if (end < 0)
        {
            throw Malformed("the hash segment is missing");
        }

        byte[] salt = DecodeSegment(rest[..end], MinSaltSize, MaxSaltSize, "salt");

        ReadOnlySpan<char> hashSegment = rest[(end + 1)..];
        if (hashSegment.Contains('$'))
        {
            throw Malformed("segments follow the hash");
        }

        byte[] hash = DecodeSegment(hashSegment, MinHashSize, MaxHashSize, "hash");
        return new Pbkdf2PhcParts(iterations, salt, hash);
    }

    private static int ParseIterations(ReadOnlySpan<char> digits)
    {
        // A non-empty run of ASCII digits without a leading zero is at least 1, so only the upper
        // bound needs checking; the length cap keeps the accumulator from overflowing.
        if (digits.IsEmpty || digits.Length > MaxIterationDigits || digits[0] == '0')
        {
            throw IterationsOutOfRange();
        }

        int value = 0;
        foreach (char digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
            {
                throw IterationsOutOfRange();
            }

            value = (value * 10) + (digit - '0');
        }

        return value <= MaxIterations
            ? value
            : throw IterationsOutOfRange();
    }

    private static byte[] DecodeSegment(ReadOnlySpan<char> segment, int minBytes, int maxBytes, string name)
    {
        // Unpadded base64 of n bytes is ceil(4n/3) characters, never 1 (mod 4); for every other
        // length L the byte count is floor(3L/4). The length check comes first so the arithmetic
        // and the buffers below stay small whatever the input.
        if (segment.Length > ((maxBytes * 4) + 2) / 3 || segment.Length % 4 == 1)
        {
            throw InvalidSegment(name, minBytes, maxBytes);
        }

        int byteCount = segment.Length * 3 / 4;
        if (byteCount < minBytes)
        {
            throw InvalidSegment(name, minBytes, maxBytes);
        }

        Span<char> padded = stackalloc char[(segment.Length + 3) / 4 * 4];
        segment.CopyTo(padded);
        padded[segment.Length..].Fill('=');

        byte[] bytes = new byte[byteCount];
        if (!Convert.TryFromBase64Chars(padded, bytes, out int written) || written != byteCount)
        {
            throw InvalidSegment(name, minBytes, maxBytes);
        }

        // The BCL decoder skips whitespace and ignores the unused low bits of the last character, so
        // several spellings decode to the same bytes. Re-encoding pins the one canonical spelling.
        Span<char> canonical = stackalloc char[padded.Length];
        if (!Convert.TryToBase64Chars(bytes, canonical, out _)
            || !canonical[..segment.Length].SequenceEqual(segment))
        {
            throw InvalidSegment(name, minBytes, maxBytes);
        }

        return bytes;
    }

    private static FormatException IterationsOutOfRange()
        => Malformed(string.Create(
            CultureInfo.InvariantCulture,
            $"the iteration count is not a decimal from 1 to {MaxIterations} without sign or leading zero"));

    private static FormatException InvalidSegment(string name, int minBytes, int maxBytes)
        => Malformed(string.Create(
            CultureInfo.InvariantCulture,
            $"the {name} is not {minBytes} to {maxBytes} bytes of canonical standard base64 without padding"));

    private static FormatException Malformed(string reason)
        => new($"The encoded password hash is not a valid PBKDF2-SHA256 PHC string: {reason}.");
}

/// <summary>
/// The parameters and values of a parsed PBKDF2-HMAC-SHA256 PHC string.
/// </summary>
/// <param name="Iterations">The iteration count, 1 to <see cref="Pbkdf2Phc.MaxIterations"/>.</param>
/// <param name="Salt">The salt bytes.</param>
/// <param name="Hash">The derived bytes to compare against.</param>
internal sealed record Pbkdf2PhcParts(int Iterations, ReadOnlyMemory<byte> Salt, ReadOnlyMemory<byte> Hash);
