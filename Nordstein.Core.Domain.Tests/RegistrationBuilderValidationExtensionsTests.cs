using System.ComponentModel.DataAnnotations;
using Autofac;
using Autofac.Core;
using AwesomeAssertions;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class RegistrationBuilderValidationExtensionsTests
{
    [TestMethod]
    public void ValidateOnActivation_InvalidObject_ThrowsDependencyResolutionWrappingValidation()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Probe>().AsSelf().WithParameter("valid", false).ValidateOnActivation();
        using IContainer container = builder.Build();

        Func<Probe> act = () => container.Resolve<Probe>();

        act.Should().Throw<DependencyResolutionException>()
            .WithInnerException<ValidationException>()
            .WithMessage("*probe is invalid*");
    }

    [TestMethod]
    public void ValidateOnActivation_ValidObject_Resolves()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Probe>().AsSelf().WithParameter("valid", true).ValidateOnActivation();
        using IContainer container = builder.Build();

        Probe probe = container.Resolve<Probe>();

        probe.Validated.Should().Be(1);
    }

    [TestMethod]
    public void ValidateOnActivation_NonValidatable_Resolves()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<NotValidatable>().AsSelf().ValidateOnActivation();
        using IContainer container = builder.Build();

        Func<NotValidatable> act = () => container.Resolve<NotValidatable>();

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ValidateOnActivation_ValidatableObjectWithBrokenAttribute_Throws()
    {
        // Validator.ValidateObject runs with validateAllProperties, so attribute rules count too.
        var builder = new ContainerBuilder();
        builder.RegisterType<AnnotatedValidatable>().AsSelf().ValidateOnActivation();
        using IContainer container = builder.Build();

        Func<AnnotatedValidatable> act = () => container.Resolve<AnnotatedValidatable>();

        act.Should().Throw<DependencyResolutionException>().WithInnerException<ValidationException>();
    }

    [TestMethod]
    public void ValidateOnActivation_ServiceRegisteredAsInterface_ValidatesTheImplementation()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Probe>().As<IProbe>().WithParameter("valid", false).ValidateOnActivation();
        using IContainer container = builder.Build();

        Func<IProbe> act = () => container.Resolve<IProbe>();

        act.Should().Throw<DependencyResolutionException>().WithInnerException<ValidationException>();
    }

    [TestMethod]
    public void ValidateOnActivation_ChainedBeforeOtherCallbacks_KeepsThemRunning()
    {
        var builder = new ContainerBuilder();
        int activated = 0;
        builder.RegisterType<Probe>()
            .AsSelf()
            .WithParameter("valid", true)
            .ValidateOnActivation()
            .OnActivated(_ => activated++);
        using IContainer container = builder.Build();

        container.Resolve<Probe>();

        activated.Should().Be(1);
    }

    [TestMethod]
    public void ValidateOnActivation_NullRegistration_Throws()
    {
        Autofac.Builder.IRegistrationBuilder<Probe, Autofac.Builder.ConcreteReflectionActivatorData, Autofac.Builder.SingleRegistrationStyle> missing =
            (new Autofac.Builder.IRegistrationBuilder<Probe, Autofac.Builder.ConcreteReflectionActivatorData, Autofac.Builder.SingleRegistrationStyle>[1])[0];

        Action act = () => RegistrationBuilderValidationExtensions.ValidateOnActivation(missing);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registration");
    }

    private interface IProbe;

    private sealed class Probe : IProbe, IValidatableObject
    {
        private readonly bool valid;

        public Probe(bool valid) => this.valid = valid;

        public int Validated { get; private set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            Validated++;
            if (!valid)
            {
                yield return new ValidationResult("The probe is invalid.");
            }
        }
    }

    // Only an IValidatableObject is validated: the helper is the domain foundation's rule, and every
    // domain object is one. An attribute on anything else is left alone.
    private sealed class NotValidatable
    {
        [Required]
        public string? Name { get; init; }
    }

    private sealed class AnnotatedValidatable : IValidatableObject
    {
        [Required]
        public string? Name { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
    }
}
