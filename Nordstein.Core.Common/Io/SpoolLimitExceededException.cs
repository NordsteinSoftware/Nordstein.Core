using System.Globalization;

namespace Nordstein.Core.Common.Io;

/// <summary>
/// Thrown by <see cref="IPrivateFileSpool.SpoolAsync"/> the moment the content grows past its byte
/// cap; the rest of the stream is not read and the partial file is deleted.
/// </summary>
public sealed class SpoolLimitExceededException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolLimitExceededException"/> class.
    /// </summary>
    /// <param name="maxBytes">The cap the content exceeded.</param>
    public SpoolLimitExceededException(long maxBytes)
        : base(string.Create(CultureInfo.InvariantCulture, $"The content exceeds {maxBytes} bytes."))
    {
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the cap the content exceeded.
    /// </summary>
    public long MaxBytes { get; }
}
