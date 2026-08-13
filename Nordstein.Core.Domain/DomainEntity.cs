using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Domain;

public abstract record DomainEntity<TSelf> : IDomainEntity<TSelf>
    where TSelf : class, IDomainEntity
{
    private readonly IRepository<TSelf> repository;

    protected DomainEntity(IRepository<TSelf> repository)
    {
        this.repository = repository;
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        IsArchived = false;
    }

    protected DomainEntity(IDomainEntityData existing, IRepository<TSelf> repository)
    {
        this.repository = repository;
        Id = existing.Id;
        CreatedAt = existing.CreatedAt;
        UpdatedAt = existing.UpdatedAt;
        IsArchived = existing.IsArchived;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public bool IsArchived { get; }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotDefault(Id);
        yield return Validation.InPast(CreatedAt);
        yield return Validation.InPast(UpdatedAt);
        yield return Validation.NotBefore(UpdatedAt, CreatedAt);
    }

    public Task<TSelf> ReloadAsync(CancellationToken cancellationToken = default)
        => repository.GetAsync(Id, cancellationToken);

    public Task<TSelf> AddAsync(CancellationToken cancellationToken = default)
        => repository.AddAsync(this.As<TSelf>(), cancellationToken);

    public Task<TSelf> UpdateAsync(CancellationToken cancellationToken = default)
        => repository.UpdateAsync(this.As<TSelf>(), cancellationToken);

    protected Task<TSelf> ApplyAsync(TSelf updated, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(updated, new ValidationContext(updated), validateAllProperties: true);
        return repository.UpdateAsync(updated, cancellationToken);
    }

    public Task<TSelf> UpsertAsync(CancellationToken cancellationToken = default)
        => repository.UpsertAsync(this.As<TSelf>(), cancellationToken);

    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => repository.RemoveAsync(Id, cancellationToken);

    public virtual bool Equals(DomainEntity<TSelf>? other)
        => other is not null && EqualityContract == other.EqualityContract && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(EqualityContract, Id);
}
