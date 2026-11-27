using System.ComponentModel.DataAnnotations;
using Autofac;
using Autofac.Builder;
using Autofac.Core;

namespace Nordstein.Core.Domain;

/// <summary>
/// Autofac registration helper that gives a hand-written registration the activation validation
/// the domain foundation applies to every discovered domain object.
/// </summary>
public static class RegistrationBuilderValidationExtensions
{
    /// <summary>
    /// Validates each instance of this registration right after Autofac activates it, so an invalid
    /// object cannot be resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it on registrations written by hand (for example keyed registrations that name a
    /// constructor); <see cref="Module"/> already applies it to every entity it discovers. An
    /// instance that is an <see cref="IValidatableObject"/> (every domain object is) goes through
    /// <see cref="Validator.ValidateObject(object, ValidationContext, bool)"/> with all properties
    /// validated, so attribute rules and <see cref="IValidatableObject.Validate"/> both count. An
    /// instance of any other type is not validated.
    /// </para>
    /// <para>
    /// A failure surfaces from <c>Resolve</c> as a <see cref="DependencyResolutionException"/>
    /// wrapping the <see cref="ValidationException"/>; read it with
    /// <c>UnwrapResolutionFailures</c> from <c>Nordstein.Core.Common.DependencyInjection</c>. The
    /// callback is added with <c>OnActivated</c>, so it runs alongside any other activation callback
    /// of the registration, in registration order. Safe to call once per registration; calling it
    /// twice validates twice.
    /// </para>
    /// </remarks>
    /// <typeparam name="TLimit">The registration's most specific known service type.</typeparam>
    /// <typeparam name="TActivatorData">The registration's activator data.</typeparam>
    /// <typeparam name="TStyle">The registration style; a single registration, not a scan.</typeparam>
    /// <param name="registration">The registration to extend.</param>
    /// <returns><paramref name="registration"/>, so calls chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is <see langword="null"/>.</exception>
    public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> ValidateOnActivation<TLimit, TActivatorData, TStyle>(
        this IRegistrationBuilder<TLimit, TActivatorData, TStyle> registration)
        where TActivatorData : IConcreteActivatorData
        where TStyle : SingleRegistrationStyle
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration.OnActivated(activated =>
        {
            if (activated.Instance is IValidatableObject validatable)
            {
                Validator.ValidateObject(validatable, new ValidationContext(validatable), validateAllProperties: true);
            }
        });
    }
}
