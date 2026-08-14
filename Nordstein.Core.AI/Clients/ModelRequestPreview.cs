namespace Nordstein.Core.AI.Clients;

/// <summary>
/// A read-only snapshot of the exact request a <see cref="IModelClient"/> would send to the model:
/// the resolved model name, the messages (system prompt already merged in), and the tool definitions.
/// Built without contacting the provider, so it can be inspected for debugging.
/// </summary>
public record ModelRequestPreview(
    string Model,
    IReadOnlyList<RequestMessagePreview> Messages,
    IReadOnlyList<RequestToolPreview> Tools);

/// <summary>One message of a <see cref="ModelRequestPreview"/>, in wire shape.</summary>
public record RequestMessagePreview(
    string Role,
    string? Content,
    IReadOnlyList<RequestToolCallPreview> ToolCalls,
    string? ToolCallId);

/// <summary>One tool call of a <see cref="RequestMessagePreview"/>, in wire shape.</summary>
public record RequestToolCallPreview(string Id, string Name, string Arguments);

/// <summary>One tool definition of a <see cref="ModelRequestPreview"/>, in wire shape.</summary>
public record RequestToolPreview(string Name, string Description, string JsonSchema);
