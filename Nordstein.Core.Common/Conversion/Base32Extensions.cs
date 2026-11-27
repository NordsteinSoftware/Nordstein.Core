namespace Nordstein.Core.Common.Conversion;

/// <summary>
/// RFC 4648 base32 in lowercase without padding (alphabet <c>a</c>–<c>z</c>, <c>2</c>–<c>7</c>), for identifiers and
/// secrets that must survive case-insensitive and URL contexts. Pure and thread-safe.
/// </summary>
public static class Base32Extensions
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>
    /// Encodes <paramref name="bytes"/> as lowercase, unpadded RFC 4648 base32: ⌈8·n/5⌉ characters, the last one
    /// padded with zero bits. The empty input gives the empty string.
    /// </summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded text.</returns>
    public static string ToBase32(this ReadOnlySpan<byte> bytes)
    {
        int length = (int)(((long)bytes.Length * 8 + 4) / 5);
        char[] encoded = new char[length];
        int bits = 0;
        int value = 0;
        int at = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                encoded[at++] = Alphabet[(value >> (bits - 5)) & 31];
                bits -= 5;
            }

            value &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            encoded[at] = Alphabet[(value << (5 - bits)) & 31];
        }

        return new string(encoded);
    }

    /// <summary>Whether <paramref name="character"/> belongs to the lowercase base32 alphabet <see cref="ToBase32"/> writes.</summary>
    /// <param name="character">The character to check.</param>
    /// <returns><c>true</c> for <c>a</c>–<c>z</c> and <c>2</c>–<c>7</c>.</returns>
    public static bool IsBase32(this char character) => character is (>= 'a' and <= 'z') or (>= '2' and <= '7');
}
