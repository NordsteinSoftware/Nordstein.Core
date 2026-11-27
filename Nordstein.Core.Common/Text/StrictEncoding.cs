using System.Text;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// The shared strict encodings, so no caller builds its own copy (and no copy forgets a flag).
/// </summary>
public static class StrictEncoding
{
    /// <summary>
    /// Gets the strict UTF-8 encoding: it writes no byte-order mark, decoding invalid bytes throws
    /// <see cref="DecoderFallbackException"/> and encoding a lone surrogate throws
    /// <see cref="EncoderFallbackException"/>, instead of either becoming U+FFFD.
    /// </summary>
    /// <remarks>
    /// One instance for the process. It is read-only (<see cref="Encoding.IsReadOnly"/>): setting a
    /// fallback on it throws <see cref="InvalidOperationException"/>, so no caller can loosen it for
    /// the others; <see cref="Encoding.Clone"/> gives a writable copy. Safe for concurrent use; the
    /// encoders and decoders it hands out are not, as for every encoding. Decoding with
    /// <see cref="Encoding.GetString(byte[])"/> keeps a leading U+FEFF; a
    /// <see cref="StreamReader"/> that detects byte-order marks drops it.
    /// </remarks>
    public static UTF8Encoding Utf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
