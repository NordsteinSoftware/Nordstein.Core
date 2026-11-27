using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Clients;

/// <summary>
/// The per-request options of a model completion: the resolved model name, the tool
/// definitions offered to the model, optional <see cref="ModelSamplingParameters"/>, and an
/// optional <see cref="ResponseFormat"/>.
/// </summary>
public record ModelOptions(
    string ModelName,
    IReadOnlyList<ToolSpecification> Tools,
    ModelSamplingParameters? Sampling = null)
{
    /// <summary>
    /// The shape the request asks the model to give its reply, or <see langword="null"/> (the
    /// default) to ask for none: a client then sends no <c>response_format</c> member, so a
    /// request without one is the same as before the member existed.
    /// </summary>
    /// <remarks>
    /// Set it with an object initializer, for example
    /// <c>new ModelOptions(model, []) { ResponseFormat = format.ResponseFormat }</c>. A client
    /// renders it with <see cref="Clients.ResponseFormat.ToOpenAiCompatibleJson"/>. Part of the
    /// record's value equality.
    /// </remarks>
    public ResponseFormat? ResponseFormat { get; init; }
}
