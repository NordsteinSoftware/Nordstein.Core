using Autofac;
using AwesomeAssertions;

namespace Nordstein.Core.Domain.Tests;

/// <summary>
/// Defaults of the domain contracts that no other test reaches: the existing-entity data of a type
/// that is not archivable reads as not archived, and the factory registration skips a constructor
/// shorter than the delegate's parameters instead of failing on it.
/// </summary>
[TestClass]
public sealed class DomainContractDefaultsTests
{
    [TestMethod]
    public void IsArchived_OfEntityDataThatDoesNotDeclareIt_IsFalse()
    {
        IDomainEntityData data = new PlainEntityData(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        data.IsArchived.Should().BeFalse();
    }

    [TestMethod]
    public void RegisterFactory_WithAConstructorShorterThanTheDelegate_SkipsItAndBindsTheMatchingOne()
    {
        var builder = new ContainerBuilder();

        builder.RegisterFactory<CreateMeasured, Measured>();
        using IContainer container = builder.Build();

        container.Resolve<CreateMeasured>()(7).Value.Should().Be(7);
    }

    private delegate Measured CreateMeasured(int value);

    private sealed record PlainEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;

    private sealed class Measured
    {
        public Measured() => Value = -1;

        public Measured(int value) => Value = value;

        public int Value { get; }
    }
}
