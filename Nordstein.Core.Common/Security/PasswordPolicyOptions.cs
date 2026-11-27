using System.ComponentModel.DataAnnotations;
using Rules = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// The bounds of <see cref="IPasswordPolicy"/> and <see cref="IPasswordGenerator"/>: the shortest password
/// accepted, in code points of its NFKC form. The longest is fixed at <see cref="MaxLength"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Module"/> registers the defaults unless the host registered its own instance first; a host that
/// registers one after the module replaces them as the last registration. A host that must never run with a
/// short minimum in production (a demonstration setting with empty passwords, for example) enforces its own
/// floor before it registers the instance: the policy accepts any minimum from 0 to <see cref="MaxLength"/>.
/// </para>
/// <para>
/// Immutable and thread-safe. <see cref="Validate"/> runs when the policy or the generator is resolved, so an
/// invalid instance fails resolution with a <see cref="ValidationException"/>.
/// </para>
/// </remarks>
public sealed record PasswordPolicyOptions : IValidatableObject
{
    /// <summary>
    /// The longest password the policy accepts: 256 code points of its NFKC form.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// The default <see cref="MinLength"/>: 12 code points.
    /// </summary>
    public const int DefaultMinLength = 12;

    /// <summary>
    /// The shortest password the policy accepts, in code points of its NFKC form: <see cref="DefaultMinLength"/>
    /// by default, 0 to <see cref="MaxLength"/>. 0 admits the empty password.
    /// </summary>
    public int MinLength { get; init; } = DefaultMinLength;

    /// <summary>
    /// Refuses a <see cref="MinLength"/> outside 0 to <see cref="MaxLength"/>.
    /// </summary>
    /// <param name="validationContext">The validation context (unused).</param>
    /// <returns>One result per rule; <see cref="ValidationResult.Success"/> for a rule that holds.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Rules.InRange(MinLength, 0, MaxLength, nameof(MinLength));
    }
}
