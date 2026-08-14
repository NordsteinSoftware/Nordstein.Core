using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Clients;

/// <summary>
/// A streaming chunk from <see cref="IModelClient.StreamAsync"/>.
/// One of: <see cref="TextDelta"/>, <see cref="ToolRequested"/>, <see cref="Completed"/>.
/// </summary>
public abstract record ModelStreamUpdate;

/// <summary>A fragment of the assistant's text response.</summary>
public sealed record TextDelta(string Text) : ModelStreamUpdate;

/// <summary>The model requested a tool invocation.</summary>
public sealed record ToolRequested(ToolRequest Request) : ModelStreamUpdate;

/// <summary>The turn finished; carries usage, latency, and the provider's finish reason.</summary>
public sealed record Completed(
    TokenUsage? Usage,
    TimeSpan Latency,
    string? FinishReason) : ModelStreamUpdate;
