using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Clients;

/// <summary>
/// The per-request options of a model completion: the resolved model name, the tool
/// definitions offered to the model, and optional <see cref="ModelSamplingParameters"/>.
/// </summary>
public record ModelOptions(
    string ModelName,
    IReadOnlyList<ToolSpecification> Tools,
    ModelSamplingParameters? Sampling = null);
