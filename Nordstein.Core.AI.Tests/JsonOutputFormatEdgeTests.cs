using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AwesomeAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.AI.Serialization.Internal;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the <see cref="JsonOutputFormat"/> surface not touched by <c>JsonOutputParserTests</c>:
/// the model-facing prompt instruction (<see cref="IOutputFormat.ToPromptString"/>) and the schema
/// validation contract.
/// </summary>
[TestClass]
public sealed class JsonOutputFormatEdgeTests : BaseTest<Nordstein.Core.AI.Module>
{
    [UsedImplicitly]
    public sealed class Sample
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private IOutputFormat Format<T>()
        => GetServices().GetRequiredService<IOutputFormat.Create>()(typeof(T));

    [TestMethod]
    public void ToPromptString_ForJsonFormat_InstructsJsonAndEmbedsSchema()
    {
        IOutputFormat format = Format<Sample>();
        string schema = format.As<JsonOutputFormat>().Schema;

        string? instruction = format.ToPromptString();

        instruction.Should().NotBeNull();
        instruction.Should().StartWith("Respond only in JSON format");
        instruction.Should().Contain(schema);
    }

    [TestMethod]
    public void Validate_ForWellFormedSchema_DoesNotThrow()
    {
        IOutputFormat format = Format<Sample>();

        FluentActions.Invoking(() => format.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_ForWellFormedSchema_RunsBothSchemaChecks()
    {
        IOutputFormat format = Format<Sample>();

        // Both the not-null-or-whitespace and the well-formed-JSON checks are yielded (each returns
        // the success sentinel here, so the count — not the content — is the signal).
        ValidationResult[] results = format.Validate(new ValidationContext(format)).ToArray();

        results.Should().HaveCount(2);
    }

    [TestMethod]
    public void Schema_IsWellFormedJson()
    {
        string schema = Format<Sample>().As<JsonOutputFormat>().Schema;

        FluentActions.Invoking(() => JsonDocument.Parse(schema)).Should().NotThrow();
    }
}
