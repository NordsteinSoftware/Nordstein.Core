using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.Common.Security.Internal;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class TrackedBase64ConverterTests
{
    [TestMethod]
    public void Read_EveryDecodedArray_IsRecordedEvenWhenTheDocumentFailsLater()
    {
        List<byte[]> decoded = [];
        JsonSerializerOptions options = new() { Converters = { new TrackedBase64Converter(decoded) } };

        Action act = () => JsonSerializer.Deserialize<byte[][]>("[\"AQI=\",\"AwQ=\",oops]", options);

        act.Should().Throw<JsonException>();
        decoded.Should().HaveCount(2);
        decoded[0].Should().Equal(1, 2);
        decoded[1].Should().Equal(3, 4);
    }

    [TestMethod]
    public void Write_IsBase64()
    {
        JsonSerializerOptions options = new() { Converters = { new TrackedBase64Converter([]) } };

        JsonSerializer.Serialize(new byte[] { 1, 2, 255 }, options).Should().Be("\"AQL/\"");
    }
}
