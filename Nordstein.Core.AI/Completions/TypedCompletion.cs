namespace Nordstein.Core.AI.Completions;

/// <summary>
/// A completion parsed into <typeparamref name="TOutput"/> via the configured output format,
/// with the usage and latency of the underlying model call.
/// </summary>
public record TypedCompletion<TOutput>(TOutput? Response, TokenUsage? Usage, TimeSpan Latency);
