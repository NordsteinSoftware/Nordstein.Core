using System.ComponentModel.DataAnnotations;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// How <see cref="IMarkdownRenderer"/> writes what Markdown has no syntax for.
/// </summary>
/// <remarks>
/// Immutable. The renderer validates the options on every call and throws
/// <see cref="ValidationException"/> for a label it cannot write on one line.
/// </remarks>
public sealed record MarkdownRenderingOptions : IValidatableObject
{
    /// <summary>
    /// Gets the word a page break's label starts with, such as <c>Page</c>: a
    /// <see cref="StructureKind.PageBreak"/> renders as a <c>---</c> rule followed by the block
    /// <c>{PageLabel} {Level}</c> (<c>Page 2</c>). <see langword="null"/> (the default) renders the
    /// rule alone. When set, not blank and without a line break.
    /// </summary>
    public string? PageLabel { get; init; }

    /// <summary>
    /// Refuses a blank page label or one with a line break, which would not render as one line.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>A result naming <see cref="PageLabel"/> when it is unusable; nothing otherwise.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PageLabel is not null && (string.IsNullOrWhiteSpace(PageLabel) || PageLabel.AsSpan().IndexOfAny('\r', '\n') >= 0))
        {
            yield return new ValidationResult($"{nameof(PageLabel)} must be null or one line of text", [nameof(PageLabel)]);
        }
    }
}
