using System.Collections;
using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsCollectionTests
{
    [TestMethod]
    public void Count_ReflectsTheNumberOfArguments()
    {
        var toolArgs = new ToolArguments([
            JsonArg("a", required: true, """{"type":"string"}"""),
            JsonArg("b", required: false, """{"type":"number"}""")
        ]);

        toolArgs.Count.Should().Be(2);
    }

    [TestMethod]
    public void Indexer_ReturnsTheArgumentAtThatPosition()
    {
        JsonToolArgument a = JsonArg("a", required: true, """{"type":"string"}""");
        JsonToolArgument b = JsonArg("b", required: false, """{"type":"number"}""");
        var toolArgs = new ToolArguments([a, b]);

        toolArgs[0].Should().BeSameAs(a);
        toolArgs[1].Should().BeSameAs(b);
    }

    [TestMethod]
    public void GenericEnumerator_YieldsArgumentsInOrder()
    {
        JsonToolArgument a = JsonArg("a", required: true, """{"type":"string"}""");
        JsonToolArgument b = JsonArg("b", required: false, """{"type":"number"}""");
        var toolArgs = new ToolArguments([a, b]);

        // Exercises IEnumerable<IToolArgument>.GetEnumerator.
        toolArgs.ToList().Should().Equal(a, b);
    }

    [TestMethod]
    public void NonGenericEnumerator_YieldsTheSameArguments()
    {
        JsonToolArgument a = JsonArg("a", required: true, """{"type":"string"}""");
        JsonToolArgument b = JsonArg("b", required: false, """{"type":"number"}""");
        IEnumerable nonGeneric = new ToolArguments([a, b]);

        var collected = new List<object?>();
        foreach (object? item in nonGeneric)
        {
            collected.Add(item);
        }

        collected.Should().HaveCount(2);
        collected[0].Should().BeSameAs(a);
        collected[1].Should().BeSameAs(b);
    }

    [TestMethod]
    public void GenericEnumerator_OnNone_IsEmpty()
    {
        ToolArguments.None.ToList().Should().BeEmpty();
    }

    private static JsonToolArgument JsonArg(string name, bool required, string schema)
    {
        using JsonDocument doc = JsonDocument.Parse(schema);
        return new JsonToolArgument(name, required, doc.RootElement);
    }
}
