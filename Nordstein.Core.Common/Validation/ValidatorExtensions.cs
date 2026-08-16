using System.ComponentModel.DataAnnotations;

namespace Nordstein.Core.Common.Validation;

/// <summary>
/// Extension methods for <see cref="IValidatableObject"/> that provide a concise way to trigger
/// full object validation via the <see cref="Validator"/> framework.
/// </summary>
public static class ValidatorExtensions
{
    /// <summary>
    /// Validates all properties of <paramref name="validatableObject"/> and throws if any
    /// validation rule is violated.
    /// </summary>
    /// <param name="validatableObject">The object to validate. Must not be <c>null</c>.</param>
    /// <exception cref="ValidationException">
    /// Thrown when one or more properties fail validation. The exception message describes the
    /// first failing rule encountered.
    /// </exception>
    /// <remarks>
    /// Equivalent to calling
    /// <c>Validator.ValidateObject(obj, new ValidationContext(obj), validateAllProperties: true)</c>.
    /// All properties — not just those with data annotation attributes — are evaluated, including
    /// custom rules returned by <see cref="IValidatableObject.Validate"/>.
    /// </remarks>
    public static void Validate(this IValidatableObject validatableObject)
        => Validator.ValidateObject(validatableObject, new ValidationContext(validatableObject), true);
}
