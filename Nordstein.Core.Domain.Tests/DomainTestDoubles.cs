using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain.Tests;

// A DomainEntity subclass that surfaces the protected ApplyAsync so it can be exercised
// directly. It deliberately does NOT implement ITestEntity, so the domain module's
// assembly scan never treats it as a second implementation of that interface.
internal sealed record ProbeEntity : DomainEntity<ITestEntity>
{
    public ProbeEntity(IRepository<ITestEntity> repository) : base(repository)
    {
    }

    public Task<ITestEntity> InvokeApplyAsync(ITestEntity updated, CancellationToken cancellationToken)
        => ApplyAsync(updated, cancellationToken);
}

// A pure domain object (no identity) plus its generator, so the module's object-generator
// discovery branch has something to find and register.
internal interface ITestValueObject : IDomainObject;

internal sealed record TestValueObject : ITestValueObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

internal sealed class TestValueObjectGenerator : DomainObjectGenerator<ITestValueObject>
{
    public TestValueObjectGenerator(IRandom random) : base(random)
    {
    }

    public override Task<ITestValueObject> CreateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ITestValueObject>(new TestValueObject());
}

// A discovered entity whose Validate ALWAYS fails, used to prove the domain module's OnActivated
// hook runs Validator.ValidateObject on every resolved entity and rejects invalid ones. Nothing
// resolves it except the negative test, so its always-failing validation never touches other tests.
internal interface IAlwaysInvalidEntity : IArchivable;

internal sealed record AlwaysInvalidEntity : DomainEntity<IAlwaysInvalidEntity>, IAlwaysInvalidEntity
{
    public AlwaysInvalidEntity(IRepository<IAlwaysInvalidEntity> repository) : base(repository)
    {
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return new ValidationResult("Always invalid.", [nameof(Id)]);
    }
}

internal sealed class AlwaysInvalidEntityGenerator : DomainEntityGenerator<IAlwaysInvalidEntity>
{
    public AlwaysInvalidEntityGenerator(IRepository<IAlwaysInvalidEntity> repository, IRandom random)
        : base(repository, random)
    {
    }

    public override Task<IAlwaysInvalidEntity> GenerateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IAlwaysInvalidEntity>(new AlwaysInvalidEntity(repository));
}
