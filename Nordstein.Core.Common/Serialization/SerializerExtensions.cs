using System.Text;
using Nordstein.Core.Common.Async;

namespace Nordstein.Core.Common.Serialization;

/// <summary>
/// Convenience extension methods for <see cref="ISerializer"/> that add string-based and
/// synchronous overloads on top of the core stream-based async contract.
/// </summary>
public static class SerializerExtensions
{
    /// <summary>
    /// Serializes <paramref name="obj"/> and returns the result as a UTF-8 string.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="serializer">The serializer to use.</param>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{String}"/> that completes with the serialized UTF-8 string.</returns>
    public static async ValueTask<string> SerializeAsync<T>(
        this ISerializer serializer,
        T obj,
        CancellationToken cancellationToken = default)
    {
        await using var result = await serializer.SerializeAsync(obj, cancellationToken);
        using var reader = new StreamReader(result);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from a UTF-8 encoded string.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="serializer">The serializer to use.</param>
    /// <param name="serialized">The UTF-8 string containing the serialized data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{T}"/> that completes with the deserialized object, or <c>null</c> if
    /// the string content deserializes to null.
    /// </returns>
    public static async ValueTask<T?> DeserializeAsync<T>(
        this ISerializer serializer,
        string serialized,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serialized));
        return await serializer.DeserializeAsync<T>(stream, cancellationToken);
    }

    /// <summary>
    /// Synchronously serializes <paramref name="obj"/> and returns the result as a UTF-8 string.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="serializer">The serializer to use.</param>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>The serialized UTF-8 string.</returns>
    /// <remarks>
    /// This method blocks the calling thread. Prefer <see cref="SerializeAsync{T}"/> in async contexts
    /// to avoid deadlocks and thread-pool starvation.
    /// </remarks>
    public static string Serialize<T>(this ISerializer serializer, T obj)
        => SerializeAsync(serializer, obj).SynchronouslyAwait();

    /// <summary>
    /// Synchronously deserializes an object of type <typeparamref name="T"/> from a UTF-8 string.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="serializer">The serializer to use.</param>
    /// <param name="serialized">The UTF-8 string containing the serialized data.</param>
    /// <returns>The deserialized object, or <c>null</c> if the content deserializes to null.</returns>
    /// <remarks>
    /// This method blocks the calling thread. Prefer <see cref="DeserializeAsync{T}"/> in async contexts.
    /// </remarks>
    public static T? Deserialize<T>(this ISerializer serializer, string serialized)
        =>
            serializer.DeserializeAsync<T>(serialized).SynchronouslyAwait();

    /// <summary>
    /// Synchronously deserializes an object of type <typeparamref name="T"/> from a UTF-8 string
    /// and throws if the result is <c>null</c>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="serializer">The serializer to use.</param>
    /// <param name="serialized">The UTF-8 string containing the serialized data.</param>
    /// <returns>The deserialized object. Never <c>null</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when deserialization succeeds but the resulting value is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method blocks the calling thread. Prefer <see cref="DeserializeAsync{T}"/> in async contexts.
    /// </remarks>
    public static T DeserializeRequired<T>(this ISerializer serializer, string serialized)
        =>
            serializer.Deserialize<T>(serialized)
           ?? throw new InvalidOperationException($"Deserialization of type {typeof(T).FullName} resulted in null.");
}
