using System.ComponentModel.DataAnnotations;

namespace Nordstein.Core.AI.Serialization;

/// <summary>
/// A format for structured model output: parses and validates the model's text into a typed
/// value, and tells the model how to shape that text in the first place.
/// </summary>
public interface IOutputFormat : IValidatableObject
{
    /// <summary>
    /// Creates the <see cref="IOutputFormat"/> for <paramref name="type"/> — the string format
    /// for <see cref="string"/>, a JSON-schema-backed format for everything else.
    /// </summary>
    delegate IOutputFormat Create(Type type);

    /// <summary>
    /// Parses model output to <typeparamref name="TOutput"/> and validates it
    /// </summary>
    Task<TOutput?> ParseAsync<TOutput>(string? output, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns an instruction that tells the model how to format its output
    /// </summary>
    string? ToPromptString();
}