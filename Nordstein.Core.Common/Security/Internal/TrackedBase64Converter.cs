using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Reads a base64 JSON string into a new array, straight from the UTF-8 token, and records every array it
/// returns, so the caller can zero each one even when the document fails to parse after it was decoded.
/// </summary>
/// <remarks>Not thread-safe: one instance serves one parse.</remarks>
internal sealed class TrackedBase64Converter : JsonConverter<byte[]>
{
    private readonly List<byte[]> decoded;

    public TrackedBase64Converter(List<byte[]> decoded)
    {
        this.decoded = decoded;
    }

    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        byte[] bytes = reader.GetBytesFromBase64();
        decoded.Add(bytes);
        return bytes;
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        => writer.WriteBase64StringValue(value);
}
