using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The master key ring file, <c>nordstein-keyring-v1</c>:
/// <c>{format, ringId, keys: [{version, createdAt, status, key, canary: {nonce, ciphertext}}]}</c>, written by
/// <see cref="MasterKeyRingInitializer"/> and read by <see cref="MasterKeyRingFile"/>. <c>key</c> is 32 bytes in
/// base64; <c>canary</c> is AES-256-GCM of <see cref="CanaryPlaintext"/> under that key with empty associated
/// data, which no <see cref="FileMasterKeyRing.Wrap"/> produces (its associated data ends with four version
/// bytes), so a canary never unwraps as a value nor a value as a canary.
/// </summary>
internal static class MasterKeyRingFormat
{
    /// <summary>
    /// The format marker; a later format changes it.
    /// </summary>
    internal const string Marker = "nordstein-keyring-v1";

    /// <summary>
    /// The status of the one key new wraps use.
    /// </summary>
    internal const string Active = "active";

    /// <summary>
    /// The status of a key that only unwraps.
    /// </summary>
    internal const string DecryptOnly = "decrypt-only";

    /// <summary>
    /// The master-key length in bytes (AES-256).
    /// </summary>
    internal const int KeySize = 32;

    /// <summary>
    /// The AES-GCM nonce length in bytes.
    /// </summary>
    internal const int NonceSize = 12;

    /// <summary>
    /// The AES-GCM tag length in bytes.
    /// </summary>
    internal const int TagSize = 16;

    /// <summary>
    /// The text each key's canary encrypts.
    /// </summary>
    internal static ReadOnlySpan<byte> CanaryPlaintext => "nordstein-keyring-canary-v1"u8;

    /// <summary>
    /// The ring file's path as configured, not made absolute: <see cref="Path.GetFullPath(string)"/> would
    /// collapse <c>link/..</c> as text and hide the reading the operating system walks from the placement check.
    /// </summary>
    internal static string ConfiguredPath(MasterKeyRingOptions options)
        => Path.Combine(options.KeyDirectory, options.FileName);

    /// <summary>
    /// Strict reading: an unknown or duplicated member is an error, so the loader and a person reading the
    /// file never disagree on a value. Every byte array the parse decodes is added to <paramref name="decoded"/>
    /// for the caller to zero. Built per load; a ring loads once per process.
    /// </summary>
    internal static JsonSerializerOptions ReadOptions(List<byte[]> decoded) => new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new TrackedBase64Converter(decoded) },
    };
}

/// <summary>
/// The ring document. Every member is nullable so a missing one is reported as malformed, not defaulted.
/// </summary>
internal sealed record MasterKeyRingDocument
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("ringId")]
    public string? RingId { get; init; }

    [JsonPropertyName("keys")]
    public IReadOnlyList<MasterKeyDocument?>? Keys { get; init; }
}

/// <summary>
/// One key entry. <see cref="Key"/> is decoded straight from the file's UTF-8 bytes, never through a string
/// (<see cref="TrackedBase64Converter"/>), and the reader zeroes it after use.
/// </summary>
internal sealed record MasterKeyDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("key")]
    public byte[]? Key { get; init; }

    [JsonPropertyName("canary")]
    public MasterKeyCanaryDocument? Canary { get; init; }
}

/// <summary>
/// A key's canary: the nonce and the ciphertext with its tag.
/// </summary>
internal sealed record MasterKeyCanaryDocument
{
    [JsonPropertyName("nonce")]
    public byte[]? Nonce { get; init; }

    [JsonPropertyName("ciphertext")]
    public byte[]? Ciphertext { get; init; }
}
