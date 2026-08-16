using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.AI.Messages;

/// <summary>
/// One content part of a <see cref="Message"/>: either text or a binary payload (an image).
/// </summary>
public sealed record Content : IDomainObject
{
    /// <summary>Whether this part is text or an image, derived from which payload is present.</summary>
    public ContentKind Kind
        => Data != null ? ContentKind.Image : ContentKind.Text;

    /// <summary>The text payload; <see langword="null"/> for image parts.</summary>
    public string? Text { get; }

    /// <summary>The binary payload with its media type; <see langword="null"/> for text parts.</summary>
    public BinaryData? Data { get; }

    private Content(
        string? text,
        BinaryData? data)
    {
        Text = text;
        Data  = data;
    }
    
    /// <summary>
    /// Creates a text content part.
    /// </summary>
    /// <param name="text">The text payload; must not be <see langword="null"/> or whitespace.</param>
    /// <returns>A new <see cref="Content"/> of kind <see cref="ContentKind.Text"/>.</returns>
    public static Content FromText(string text)
        => new(text, null);

    /// <summary>
    /// Creates an image content part.
    /// </summary>
    /// <param name="data">The binary image payload; must carry a non-empty <see cref="BinaryData.MediaType"/>.</param>
    /// <returns>A new <see cref="Content"/> of kind <see cref="ContentKind.Image"/>.</returns>
    public static Content FromImage(BinaryData data)
        => new(null, data);

    /// <summary>
    /// Validates that exactly one of <see cref="Text"/> or <see cref="Data"/> is set, and that
    /// the set payload satisfies its own non-emptiness rules. Yields
    /// <c>Validation.Success</c> entries on success.
    /// </summary>
    /// <param name="validationContext">The validation context provided by the framework.</param>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Text.NotNullOrWhiteSpace() && Data != null)
        {
            yield return new ValidationResult(
                $"Content cannot have both text and data. Text: '{Text}', Data length: {Data.Length}",
                [nameof(Text), nameof(Data)]);
            yield break;
        }

        if (Text.NullOrWhiteSpace() && Data is null)
        {
            yield return new ValidationResult(
                "Content must have either text or data.",
                [nameof(Text), nameof(Data)]);
            yield break;
        }
        
        if (Kind is ContentKind.Text)
        {
            yield return Validation.NotNullOrWhiteSpace(Text);
            yield return Validation.NotEmpty(Text);
        }
        
        if (Kind is ContentKind.Image)
        {
            yield return Validation.NotNull(Data);
            yield return Validation.NotNullOrWhiteSpace(Data?.MediaType);
        }
    }

    /// <inheritdoc />
    public bool Equals(Content? other)
        => other is not null &&
           Equals(Kind, other.Kind) &&
           Equals(Text, other.Text) &&
           Equals(Data?.MediaType, other.Data?.MediaType) &&
           ((Data == null && other.Data == null) ||
            (Data?.ToArray() ?? []).SequenceEqual(other.Data?.ToArray() ?? []));

    /// <inheritdoc />
    public override int GetHashCode()
        // Hash Data by length, not reference: Equals compares the bytes, so equal content (same bytes
        // ⇒ same length) hashes equally; differing bytes of equal length may collide, which is allowed.
        => HashCode.Combine((int)Kind, Text, Data?.Length, Data?.MediaType);

    public override string ToString()
        => Kind switch
        {
            ContentKind.Text => Text,
            ContentKind.Image => $"Image: MediaType='{Data?.MediaType}', Size={Data?.Length} bytes",
            _ => "Unknown content"
        } ?? string.Empty;
}