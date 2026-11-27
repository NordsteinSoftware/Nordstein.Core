using System.Text;

namespace Nordstein.Core.Common.Io;

/// <summary>
/// Reads a text payload under a character cap without holding more than the cap in memory.
/// </summary>
public static class StreamTextExtensions
{
    private const int BufferChars = 1 << 14;

    /// <summary>
    /// Reads <paramref name="stream"/> to its end as UTF-8 text with line endings normalized to <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// A UTF-8, UTF-16 or UTF-32 byte order mark selects its encoding and is stripped; without one the
    /// bytes are UTF-8 and invalid sequences become U+FFFD. <c>\r\n</c> and a lone <c>\r</c> each become
    /// one <c>\n</c>, also when the two characters of a <c>\r\n</c> arrive in different reads.
    /// <paramref name="maxCharacters"/> caps the normalized text: the read throws as soon as one more
    /// character would be kept, before the rest of the stream is read. Not thread-safe on one stream,
    /// like the stream itself.
    /// </remarks>
    /// <param name="stream">The readable stream, read from its current position.</param>
    /// <param name="maxCharacters">The most characters the returned text may hold; 0 allows only empty text.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open; otherwise it is disposed.</param>
    /// <param name="cancellationToken">Cancels the read between and during reads.</param>
    /// <returns>The normalized text, at most <paramref name="maxCharacters"/> long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCharacters"/> is negative.</exception>
    /// <exception cref="TextLimitExceededException">The text is longer than <paramref name="maxCharacters"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<string> ReadAllTextAsync(this Stream stream, int maxCharacters, bool leaveOpen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen);
        var text = new StringBuilder();
        char[] buffer = new char[BufferChars];
        // a \r closed the previous read: a \n opening this one belongs to it
        bool afterCarriageReturn = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return text.ToString();
            }

            for (int index = 0; index < read; index++)
            {
                char next = buffer[index];
                bool skipped = afterCarriageReturn && next == '\n';
                afterCarriageReturn = next == '\r';
                if (skipped)
                {
                    continue;
                }

                if (text.Length == maxCharacters)
                {
                    throw new TextLimitExceededException(maxCharacters);
                }

                text.Append(afterCarriageReturn ? '\n' : next);
            }
        }
    }
}
