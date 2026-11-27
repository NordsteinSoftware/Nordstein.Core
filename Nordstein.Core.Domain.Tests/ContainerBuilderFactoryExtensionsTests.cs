using System.ComponentModel.DataAnnotations;
using Autofac;
using Autofac.Core;
using AwesomeAssertions;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class ContainerBuilderFactoryExtensionsTests
{
    [TestMethod]
    public void RegisterFactory_SameArityConstructors_EachDelegateBindsItsOwn()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateById, Pair>();
        builder.RegisterFactory<CreateByName, Pair>();
        using IContainer container = builder.Build();
        var id = Guid.NewGuid();

        Pair byId = container.Resolve<CreateById>()(id);
        Pair byName = container.Resolve<CreateByName>()("seefeld");

        byId.Made.Should().Be($"id {id}");
        byName.Made.Should().Be("name seefeld");
    }

    [TestMethod]
    public void RegisterFactory_ADelegateReturningAnInterface_CreatesTheImplementation()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateNamed, Pair>();
        using IContainer container = builder.Build();

        INamed named = container.Resolve<CreateNamed>()("tirol");

        named.Should().BeOfType<Pair>().Which.Made.Should().Be("name tirol");
    }

    [TestMethod]
    public void RegisterFactory_TwoParametersOfOneType_BoundByPosition()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateFlags, Flags>();
        using IContainer container = builder.Build();

        Flags flags = container.Resolve<CreateFlags>()(true, false);

        (flags.First, flags.Second).Should().Be((true, false));
    }

    [TestMethod]
    public void RegisterFactory_OptionalParameterOfARegisteredType_StaysThePassedValue()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Dependency>().As<IDependency>();
        builder.RegisterFactory<CreateWithOptional, WithOptional>();
        using IContainer container = builder.Build();

        WithOptional made = container.Resolve<CreateWithOptional>()(Guid.NewGuid(), null);

        made.Extra.Should().BeNull();
    }

    [TestMethod]
    public void RegisterFactory_FixedTypedParameters_TellTwoPathsOnOneConstructorApart()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateAdmin, Principal>(TypedParameter.From(PrincipalKind.Admin));
        builder.RegisterFactory<CreateSystem, Principal>(TypedParameter.From(PrincipalKind.System));
        using IContainer container = builder.Build();

        container.Resolve<CreateAdmin>()().Kind.Should().Be(PrincipalKind.Admin);
        container.Resolve<CreateSystem>()().Kind.Should().Be(PrincipalKind.System);
    }

    [TestMethod]
    public void RegisterFactory_RemainingParameters_ComeFromTheContainer_FewestParametersWin()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Dependency>().As<IDependency>();
        builder.RegisterFactory<CreateNeedy, Needy>();
        using IContainer container = builder.Build();

        Needy made = container.Resolve<CreateNeedy>()(7);

        made.Number.Should().Be(7);
        made.Dependency.Should().BeOfType<Dependency>();
    }

    [TestMethod]
    public void RegisterFactory_InvalidObject_ThrowsDependencyResolutionWrappingValidation()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateValidated, Validated>();
        using IContainer container = builder.Build();

        Action act = () => container.Resolve<CreateValidated>()(false);

        act.Should().Throw<DependencyResolutionException>().WithInnerException<ValidationException>();
    }

    [TestMethod]
    public void RegisterFactory_TheImplementationIsNotResolvableAsItself()
    {
        var builder = new ContainerBuilder();
        builder.RegisterFactory<CreateAdmin, Principal>(TypedParameter.From(PrincipalKind.Admin));
        using IContainer container = builder.Build();

        container.IsRegistered<Principal>().Should().BeFalse();
    }

    [TestMethod]
    public void RegisterFactory_NoMatchingConstructor_Throws_NamingTheDelegate()
    {
        var builder = new ContainerBuilder();

        Action act = () => builder.RegisterFactory<CreateByNumber, Pair>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(CreateByNumber)}*");
    }

    [TestMethod]
    public void RegisterFactory_NullableIsNotItsUnderlyingType()
    {
        var builder = new ContainerBuilder();

        Action act = () => builder.RegisterFactory<CreateByMaybeId, Pair>();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void RegisterFactory_TwoConstructorsWithTheFewestParameters_Throws_NamingTheCandidates()
    {
        var builder = new ContainerBuilder();

        Action act = () => builder.RegisterFactory<CreateTied, Tied>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IDependency*IOther*");
    }

    [TestMethod]
    public void RegisterFactory_ReturnTypeNotAssignable_Throws()
    {
        var builder = new ContainerBuilder();

        Action act = () => builder.RegisterFactory<CreateById, Flags>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(Flags)}*");
    }

    [TestMethod]
    public void RegisterFactory_AFixedArgumentThatIsNoTypedParameter_Throws()
    {
        var builder = new ContainerBuilder();

        Action act = () => builder.RegisterFactory<CreateAdmin, Principal>(new NamedParameter("kind", PrincipalKind.Admin));

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void RegisterFactory_NullArguments_Throw()
    {
        Action noBuilder = () => NullOf<ContainerBuilder>().RegisterFactory<CreateById, Pair>();
        Action noFixed = () => new ContainerBuilder().RegisterFactory<CreateById, Pair>(NullOf<Parameter[]>());

        noBuilder.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        noFixed.Should().Throw<ArgumentNullException>().WithParameterName("fixedArguments");
    }

    // a null where the signature promises none, without the `!` operator
    private static T NullOf<T>()
        where T : class => (new T[1])[0];

    private delegate Pair CreateById(Guid id);

    private delegate Pair CreateByName(string name);

    private delegate INamed CreateNamed(string name);

    private delegate Pair CreateByNumber(int number);

    private delegate Pair CreateByMaybeId(Guid? id);

    private delegate Flags CreateFlags(bool first, bool second);

    private delegate WithOptional CreateWithOptional(Guid id, IDependency? extra);

    private delegate Principal CreateAdmin();

    private delegate Principal CreateSystem();

    private delegate Needy CreateNeedy(int number);

    private delegate Validated CreateValidated(bool valid);

    private delegate Tied CreateTied(Guid id);

    private enum PrincipalKind
    {
        Admin = 1,
        System = 2,
    }

    private interface IDependency;

    private interface IOther;

    private interface INamed
    {
        string Made { get; }
    }

    private sealed class Dependency : IDependency;

    private sealed class Pair : INamed
    {
        public Pair(Guid id) => Made = $"id {id}";

        public Pair(string name) => Made = $"name {name}";

        public string Made { get; }
    }

    private sealed class Flags(bool first, bool second)
    {
        public bool First { get; } = first;

        public bool Second { get; } = second;
    }

    private sealed class WithOptional(Guid id, IDependency? extra = null)
    {
        public Guid Id { get; } = id;

        public IDependency? Extra { get; } = extra;
    }

    private sealed class Principal(PrincipalKind kind)
    {
        public PrincipalKind Kind { get; } = kind;
    }

    private sealed class Needy
    {
        public Needy(int number, IDependency dependency)
        {
            Number = number;
            Dependency = dependency;
        }

        public Needy(int number, IDependency dependency, IOther other)
            : this(number, dependency) => Other = other;

        public int Number { get; }

        public IDependency Dependency { get; }

        public IOther? Other { get; }
    }

    private sealed class Tied
    {
        public Tied(Guid id, IDependency dependency) => Id = id;

        public Tied(Guid id, IOther other) => Id = id;

        public Guid Id { get; }
    }

    private sealed class Validated(bool valid) : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!valid)
            {
                yield return new ValidationResult("The probe is invalid.");
            }
        }
    }
}
