using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordstein.Core.Domain.Paging;

/// <summary>
/// Encodes a keyset paging position into an opaque cursor string and decodes it back.
/// </summary>
/// <remarks>
/// <para>
/// A cursor is unpadded base64url (RFC 4648 §5) of the UTF-8 JSON of the position, written with
/// <c>System.Text.Json</c> under fixed, strict options that callers cannot change: member names as
/// declared (case-sensitive), no unknown, duplicate or missing required members, no
/// <see langword="null"/> where the type does not allow one, no comments and no trailing data.
/// JSON formats are culture-invariant, so a cursor does not depend on the current culture.
/// </para>
/// <para>
/// A cursor is <b>neither signed nor encrypted</b>. Anyone can read it and anyone can forge one.
/// Treat a decoded position as untrusted input: re-apply access control on every page and
/// validate what you decode (for example, that a value has the type its key requires).
/// </para>
/// <para>
/// The position type of a <c>Decode</c> must match the one the cursor
/// was encoded from. Use a record with a constructor per position shape; positions must be
/// serializable by <c>System.Text.Json</c>'s reflection-based serializer.
/// </para>
/// <para>All members are thread-safe.</para>
/// </remarks>
public static class OpaqueCursor
{
    /// <summary>
    /// The maximum length, in characters, of a cursor that <c>Encode</c> writes and
    /// <c>Decode</c> accepts.
    /// </summary>
    public const int MaxLength = 4096;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    /// Encodes <paramref name="position"/> as an opaque cursor.
    /// </summary>
    /// <typeparam name="T">The position type, usually a record.</typeparam>
    /// <param name="position">The position to encode; must not be <see langword="null"/>.</param>
    /// <returns>
    /// A non-empty, unpadded base64url string of at most <see cref="MaxLength"/> characters that
    /// <see cref="Decode{T}(string)"/> turns back into an equal position.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="position"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The cursor would be longer than <see cref="MaxLength"/> characters.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> cannot be serialized by <c>System.Text.Json</c>.
    /// </exception>
    public static string Encode<T>(T position)
    {
        ArgumentNullException.ThrowIfNull(position);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(position, Options);
        if (Base64Url.GetEncodedLength(json.Length) > MaxLength)
        {
            throw new ArgumentException(
                $"The encoded position is longer than {MaxLength} characters.",
                nameof(position));
        }

        return Base64Url.EncodeToString(json);
    }

    /// <summary>
    /// Decodes a cursor written by <see cref="Encode{T}(T)"/> back into its position.
    /// </summary>
    /// <typeparam name="T">The position type the cursor was encoded from.</typeparam>
    /// <param name="cursor">The cursor, as received from an untrusted client.</param>
    /// <returns>The decoded position; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="cursor"/> does not yield a <typeparamref name="T"/>:
    /// it is <see langword="null"/> or empty, longer than <see cref="MaxLength"/>, not canonical
    /// unpadded base64url, not UTF-8 JSON, has trailing data or comments, misses a required
    /// member, has an unknown or duplicate member, has a value of the wrong type, holds
    /// <see langword="null"/> where <typeparamref name="T"/> does not allow it, or is the JSON
    /// literal <c>null</c>. <see cref="ArgumentException.ParamName"/> is <c>"cursor"</c>, and the
    /// message never echoes the cursor's content. <c>JsonException</c> and
    /// <see cref="FormatException"/> are never thrown.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> cannot be deserialized by <c>System.Text.Json</c>.
    /// </exception>
    /// <remarks>
    /// Exceptions thrown by <typeparamref name="T"/>'s own constructor propagate unchanged.
    /// </remarks>
    public static T Decode<T>(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            throw Invalid("The cursor is empty.");
        }

        if (cursor.Length > MaxLength)
        {
            throw Invalid($"The cursor is longer than {MaxLength} characters.");
        }

        byte[] json;
        try
        {
            json = Base64Url.DecodeFromChars(cursor);
        }
        catch (FormatException exception)
        {
            throw Invalid("The cursor is not base64url.", exception);
        }

        // WHY: the decoder tolerates padding and whitespace; one position has one cursor.
        if (!string.Equals(Base64Url.EncodeToString(json), cursor, StringComparison.Ordinal))
        {
            throw Invalid("The cursor is not canonical unpadded base64url.");
        }

        T? position;
        try
        {
            position = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException exception)
        {
            throw Invalid("The cursor does not hold a valid position.", exception);
        }

        return position ?? throw Invalid("The cursor holds no position.");
    }

    private static ArgumentException Invalid(string message, Exception? inner = null)
        => new(message, "cursor", inner);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
