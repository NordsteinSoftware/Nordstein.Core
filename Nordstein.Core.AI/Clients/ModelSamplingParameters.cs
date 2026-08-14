namespace Nordstein.Core.AI.Clients;

/// <summary>
/// Per-request sampling overrides. Every member is optional; a <see langword="null"/> leaves the
/// provider's own default in place rather than sending a value.
/// </summary>
/// <remarks>
/// <para>
/// Exists so completion-shaping controls surfaced to a caller actually reach the wire:
/// <see cref="ModelOptions"/> carries only the model name and tools, so without this record any
/// sampling choice a caller made would be silently dropped before the request left the process.
/// </para>
/// <para>
/// Support is per-provider: an OpenAI-compatible backend may reject or ignore individual fields
/// (reasoning models, for instance, refuse <c>temperature</c>). Nothing is validated here — the
/// provider's own error is the honest answer, and it reaches the caller instead of the value
/// being discarded locally.
/// </para>
/// <para>
/// There is deliberately no choice-count (<c>n</c>) member. It can be put on the wire, but
/// streaming responses carry no choice index, so every completion's tokens arrive flattened into
/// one indistinguishable stream. Asking for more than one completion would bill for N and render
/// them interleaved into a single garbled message, so the parameter is not offered at all.
/// </para>
/// </remarks>
public record ModelSamplingParameters(
    double? Temperature = null,
    double? TopP = null,
    double? FrequencyPenalty = null,
    double? PresencePenalty = null,
    int? MaxOutputTokens = null,
    long? Seed = null,
    IReadOnlyList<string>? StopSequences = null,
    string? ReasoningEffort = null)
{
    /// <summary>True when no override is set, so the request carries the provider's defaults.</summary>
    public bool IsEmpty
        => Temperature is null
           && TopP is null
           && FrequencyPenalty is null
           && PresencePenalty is null
           && MaxOutputTokens is null
           && Seed is null
           && (StopSequences is null || StopSequences.Count == 0)
           && string.IsNullOrWhiteSpace(ReasoningEffort);
}
