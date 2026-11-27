using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AwesomeAssertions;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing.Fences;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// Enforces the rule from <c>docs/cryptography.md</c> ("CSPRNG, never <c>IRandom</c>"): no production
/// type outside the test-data generator surface may take an <see cref="IRandom"/> dependency, so a
/// nonce, salt, key or token can never be drawn from the seeded generator.
/// </summary>
/// <remarks>
/// The generator surface is every subclass of <see cref="DomainObjectGenerator{TDomainObject}"/>
/// (which includes <see cref="DomainEntityGenerator{TDomainEntity}"/>) plus the
/// <see cref="IRandom"/> implementations themselves (<c>SeededRandom</c>).
/// </remarks>
[TestClass]
public sealed class SeededRandomIsNotUsedForSecretsTests
{
    private const string SolutionFileName = "Nordstein.Core.sln";

    [TestMethod]
    public void ProductionTypes_OutsideGeneratorSurface_DoNotTakeIRandom()
    {
        IEnumerable<Type> productionTypes = LoadProductionAssemblies().SelectMany(assembly => assembly.GetTypes());

        IReadOnlyList<string> violations = FindViolations(productionTypes);

        violations.Should().BeEmpty(
            "nonces, salts, keys and tokens must come from RandomNumberGenerator; only test-data "
            + "generators (DomainObjectGenerator<T> subclasses) may take the seeded IRandom");
    }

    [TestMethod]
    public void FindViolations_WithDirectIRandomParameter_ReportsTheType()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(TakesRandom)]);

        violations.Should().ContainSingle().Which.Should().Contain(typeof(TakesRandom).FullName);
    }

    [TestMethod]
    public void FindViolations_WithNonPublicConstructor_ReportsTheType()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(TakesRandomPrivately)]);

        violations.Should().ContainSingle().Which.Should().Contain(typeof(TakesRandomPrivately).FullName);
    }

    [TestMethod]
    public void FindViolations_WithIRandomWrappedInGenericOrArray_ReportsTheType()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(TakesLazyRandom), typeof(TakesRandomArray)]);

        violations.Should().HaveCount(2);
        violations.Should().Contain(violation => violation.Contains(typeof(TakesLazyRandom).FullName ?? "?"));
        violations.Should().Contain(violation => violation.Contains(typeof(TakesRandomArray).FullName ?? "?"));
    }

    [TestMethod]
    public void FindViolations_WithGeneratorSubclass_IsExempt()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(ExemptGenerator)]);

        violations.Should().BeEmpty();
    }

    [TestMethod]
    public void FindViolations_WithGeneratorBaseClasses_IsExempt()
    {
        IReadOnlyList<string> violations =
            FindViolations([typeof(DomainObjectGenerator<>), typeof(DomainEntityGenerator<>)]);

        violations.Should().BeEmpty();
    }

    [TestMethod]
    public void FindViolations_WithIRandomImplementation_IsExempt()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(DecoratingRandom)]);

        violations.Should().BeEmpty();
    }

    [TestMethod]
    public void FindViolations_WithoutIRandomParameter_ReportsNothing()
    {
        IReadOnlyList<string> violations = FindViolations([typeof(TakesNoRandom)]);

        violations.Should().BeEmpty();
    }

    private static IReadOnlyList<string> FindViolations(IEnumerable<Type> types) =>
        types
            .Where(type => !IsGeneratorSurface(type))
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => MentionsIRandom(parameter.ParameterType))
                .Select(parameter => $"{type.FullName} (constructor parameter '{parameter.Name}' of type {parameter.ParameterType})"))
            .ToList();

    private static bool IsGeneratorSurface(Type type)
    {
        if (typeof(IRandom).IsAssignableFrom(type))
        {
            return true;
        }

        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(DomainObjectGenerator<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True for <see cref="IRandom"/> or anything assignable to it, also when wrapped in an array,
    /// a by-ref, or any generic argument (<c>Lazy&lt;IRandom&gt;</c>, <c>Func&lt;IRandom&gt;</c>, ...).
    /// </summary>
    private static bool MentionsIRandom(Type type)
    {
        if (type.HasElementType)
        {
            Type? elementType = type.GetElementType();
            return elementType is not null && MentionsIRandom(elementType);
        }

        if (type.IsGenericParameter)
        {
            return false;
        }

        return typeof(IRandom).IsAssignableFrom(type)
            || (type.IsGenericType && type.GetGenericArguments().Any(MentionsIRandom));
    }

    /// <summary>
    /// Loads every production project of the solution (every project but the <c>*.Tests</c>
    /// projects and the <c>Nordstein.Core.Testing</c> harness, which are test code), so a newly
    /// added package cannot escape the guard: until this test project references it, the guard fails.
    /// </summary>
    private static IReadOnlyList<Assembly> LoadProductionAssemblies()
    {
        SolutionProject[] production = [.. new SolutionFile(SolutionFileName, AppContext.BaseDirectory).Projects
            .Where(project => !project.IsTestProject
                && !string.Equals(project.Name, "Nordstein.Core.Testing", StringComparison.Ordinal))];

        production.Should().NotBeEmpty($"{SolutionFileName} must list the production projects");
        return [.. production.Select(project => project.LoadAssembly())];
    }

    private sealed class TakesRandom(IRandom random)
    {
        public IRandom Random { get; } = random;
    }

    private sealed class TakesRandomPrivately
    {
        private TakesRandomPrivately(IRandom random)
        {
            Random = random;
        }

        public IRandom Random { get; }
    }

    private sealed class TakesLazyRandom(Lazy<IRandom> random)
    {
        public Lazy<IRandom> Random { get; } = random;
    }

    private sealed class TakesRandomArray(IRandom[] randoms)
    {
        public IRandom[] Randoms { get; } = randoms;
    }

    private sealed class TakesNoRandom(int seed)
    {
        public int Seed { get; } = seed;
    }

    private sealed class ExemptGenerator(IRandom random) : DomainObjectGenerator<ExemptObject>(random)
    {
        public override Task<ExemptObject> CreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExemptObject(random.Int(min: 0, max: 10)));
    }

    private sealed record ExemptObject(int Value) : IDomainObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
    }

    private sealed class DecoratingRandom(IRandom inner) : IRandom
    {
        public bool Bool() => inner.Bool();

        public Guid Guid() => inner.Guid();

        public string String() => inner.String();

        public string UniqueString() => inner.UniqueString();

        public string Email() => inner.Email();

        public Uri Uri() => inner.Uri();

        public int Int(int? min = null, int? max = null) => inner.Int(min, max);

        public long Long(long? min = null, long? max = null) => inner.Long(min, max);

        public double Double(double? min = null, double? max = null) => inner.Double(min, max);

        public decimal Decimal(decimal? min = null, decimal? max = null) => inner.Decimal(min, max);

        public T Any<T>(IReadOnlyCollection<T> options) => inner.Any(options);

        public T Enum<T>() where T : struct, Enum => inner.Enum<T>();

        public TimeSpan TimeSpan(TimeSpan? min = null, TimeSpan? max = null) => inner.TimeSpan(min, max);

        public DateTimeOffset DateTimeOffset(DateTimeOffset? min = null, DateTimeOffset? max = null) =>
            inner.DateTimeOffset(min, max);
    }
}
