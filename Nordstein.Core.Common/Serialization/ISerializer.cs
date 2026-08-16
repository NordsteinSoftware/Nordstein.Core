using JetBrains.Annotations;

namespace Nordstein.Core.Common.Serialization;

/// <summary>
/// Contract for serializing objects to and deserializing objects from a stream.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe. The serialization format is determined by the
/// implementation (e.g. JSON, MessagePack); consumers should not depend on the wire format.
/// </remarks>
public interface ISerializer
{
    /// <summary>
    /// Serializes <paramref name="obj"/> and returns the result as a stream.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize. May be <c>null</c> if <typeparamref name="T"/> is nullable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{Stream}"/> that completes with a readable stream containing the serialized
    /// representation of <paramref name="obj"/>. The caller <b>must</b> dispose this stream when done;
    /// it is annotated <c>[MustDisposeResource]</c>.
    /// </returns>
    [MustDisposeResource]
    public ValueTask<Stream> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from <paramref name="stream"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="stream">A readable stream containing the serialized data. The caller retains ownership.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{T}"/> that completes with the deserialized object, or <c>null</c> if
    /// the stream content deserializes to a null value.
    /// </returns>
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);
}
