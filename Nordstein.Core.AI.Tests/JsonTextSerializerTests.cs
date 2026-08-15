using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using AwesomeAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the registered <see cref="ITextSerializer"/> (the JSON implementation): the
/// <see cref="ITextSerializer.Serialize"/> null and indentation branches, the string / Guid / object
/// deserialization paths (sync and async), the validation hook, and the error and cancellation paths.
/// </summary>
[TestClass]
public sealed class JsonTextSerializerTests : BaseTest<Nordstein.Core.AI.Module>
{
    public sealed class Poco
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [UsedImplicitly]
    public sealed class ValidatedPoco : IValidatableObject
    {
        public int Age { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Validation.GreaterThan(Age, 0, nameof(Age));
        }
    }

    private ITextSerializer Serializer()
        => GetServices().GetRequiredService<ITextSerializer>();

    [TestMethod]
    public void Serialize_Null_ReturnsEmptyString()
        => Serializer().Serialize(null).Should().BeEmpty();

    [TestMethod]
    public void Serialize_Object_ProducesCamelCaseJson()
    {
        string json = Serializer().Serialize(new Poco { Name = "Alice", Age = 30 });

        json.Should().Contain("\"name\":\"Alice\"");
        json.Should().Contain("\"age\":30");
    }

    [TestMethod]
    public void Serialize_WithoutIndentation_IsSingleLine()
    {
        string json = Serializer().Serialize(new Poco { Name = "Alice", Age = 30 });

        json.Should().NotContain("\n");
    }

    [TestMethod]
    public void Serialize_WithIndentation_IsMultiLine()
    {
        string json = Serializer().Serialize(new Poco { Name = "Alice", Age = 30 }, writeIndented: true);

        json.Should().Contain("\n");
    }

    [TestMethod]
    public async Task DeserializeAsync_TargetingString_ReturnsRawValue()
    {
        string? result = await Serializer().DeserializeAsync<string>("raw model text", CancellationToken);

        result.Should().Be("raw model text");
    }

    [TestMethod]
    public async Task DeserializeAsync_TargetingGuid_ParsesGuid()
    {
        Guid id = Guid.NewGuid();

        Guid result = await Serializer().DeserializeAsync<Guid>(id.ToString(), CancellationToken);

        result.Should().Be(id);
    }

    [TestMethod]
    public async Task DeserializeAsync_TargetingGuidWithInvalidText_ThrowsSerializationException()
    {
        await Serializer().Invoking(s => s.DeserializeAsync<Guid>("not-a-guid", CancellationToken))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task DeserializeAsync_TargetingObject_ReturnsDeserialized()
    {
        string json = """{"name":"Bob","age":41}""";

        Poco? result = await Serializer().DeserializeAsync<Poco>(json, CancellationToken);

        result.Should().NotBeNull();
        result.Name.Should().Be("Bob");
        result.Age.Should().Be(41);
    }

    [TestMethod]
    public async Task DeserializeAsync_MalformedJson_ThrowsSerializationException()
    {
        await Serializer().Invoking(s => s.DeserializeAsync<Poco>("{ not json", CancellationToken))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task DeserializeAsync_ValidationFailure_ThrowsSerializationException()
    {
        // The async path wraps the validation failure raised inside object parsing.
        await Serializer().Invoking(s => s.DeserializeAsync<ValidatedPoco>("""{"age":0}""", CancellationToken))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task DeserializeAsync_WithCancelledToken_ThrowsOperationCanceled()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Serializer().Invoking(s => s.DeserializeAsync<Poco>("""{"name":"Bob","age":41}""", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public void Deserialize_TargetingString_ReturnsRawValue()
        => Serializer().Deserialize<string>("raw model text").Should().Be("raw model text");

    [TestMethod]
    public void Deserialize_TargetingObject_ReturnsDeserialized()
    {
        Poco? result = Serializer().Deserialize<Poco>("""{"name":"Carol","age":22}""");

        result.Should().NotBeNull();
        result.Name.Should().Be("Carol");
        result.Age.Should().Be(22);
    }

    [TestMethod]
    public void Deserialize_ValidationFailure_ThrowsValidationException()
    {
        // The synchronous path validates and surfaces the ValidationException directly (unwrapped).
        Serializer().Invoking(s => s.Deserialize<ValidatedPoco>("""{"age":0}"""))
            .Should().Throw<ValidationException>();
    }
}
