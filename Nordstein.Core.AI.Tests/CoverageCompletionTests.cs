using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.AI.Serialization.Internal;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the <see cref="IOutputFormat.Create"/> factory delegate registered by
/// <see cref="Nordstein.Core.AI.Module"/>: <see cref="string"/> resolves to the raw
/// <see cref="StringOutputFormat"/>, every other type to a JSON-schema-backed format.
/// </summary>
[TestClass]
public sealed class OutputFormatFactoryTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public void Create_ForStringType_ReturnsStringOutputFormat()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IOutputFormat.Create>();

        IOutputFormat format = create(typeof(string));

        format.Should().BeOfType<StringOutputFormat>();
    }

    [TestMethod]
    public void Create_ForNonStringType_ReturnsJsonOutputFormat()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IOutputFormat.Create>();

        IOutputFormat format = create(typeof(FactoryPayload));

        format.Should().BeOfType<JsonOutputFormat>();
    }

    [UsedImplicitly]
    public sealed class FactoryPayload
    {
        public string Name { get; set; } = string.Empty;
    }
}

/// <summary>
/// Covers the parameterless <see cref="JsonTextSerializer"/> constructor (the converterless
/// delegating <c>: this([])</c> overload) and the validated-yet-valid deserialization path, where
/// an <see cref="IValidatableObject"/> passes validation and the value is returned rather than throwing.
/// </summary>
[TestClass]
public sealed class JsonTextSerializerConstructionTests
{
    [TestMethod]
    public void ParameterlessConstructor_ProducesUsableSerializer()
    {
        var serializer = new JsonTextSerializer();

        string json = serializer.Serialize(new Payload { Age = 7 });

        json.Should().Contain("\"age\":7");
    }

    [TestMethod]
    public void Deserialize_ValidValidatableObject_PassesValidationAndReturns()
    {
        // The validation hook runs `validatable.Validate()` and, when it passes, control must fall
        // through and return the deserialized value (the non-throwing branch of the validation hook).
        var serializer = new JsonTextSerializer();

        Payload? result = serializer.Deserialize<Payload>("""{"age":5}""");

        result.Should().NotBeNull();
        result.Age.Should().Be(5);
    }

    public sealed class Payload : IValidatableObject
    {
        public int Age { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Validation.GreaterThan(Age, 0, nameof(Age));
        }
    }
}

/// <summary>
/// Covers the "fewer than two content slots" invariant of <see cref="ToolMessage.Validate"/>, which
/// the JSON constructor can produce directly (a tool message needs the id slot plus at least one
/// result slot). This is distinct from the <c>Deconstruct</c> guard, which throws instead.
/// </summary>
[TestClass]
public sealed class ToolMessageValidationTests
{
    [TestMethod]
    public void Validate_SingleContentSlot_YieldsAtLeastTwoItemsFailure()
    {
        var message = new ToolMessage([Content.FromText("call-id")]);

        ValidationResult[] failures = message
            .Validate(new ValidationContext(message))
            .Where(result => result != ValidationResult.Success)
            .ToArray();

        failures.Should().Contain(result =>
            result != null && result.ErrorMessage == "Contents must have at least 2 items");
    }

    [TestMethod]
    public void Validate_EmptyContents_YieldsAtLeastTwoItemsFailure()
    {
        var message = new ToolMessage([]);

        ValidationResult[] failures = message
            .Validate(new ValidationContext(message))
            .Where(result => result != ValidationResult.Success)
            .ToArray();

        failures.Should().Contain(result =>
            result != null && result.ErrorMessage == "Contents must have at least 2 items");
    }
}
