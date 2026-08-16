using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Domain;

/// <summary>
/// Abstract base record for persistent domain entities.
/// </summary>
/// <remarks>
/// Manages <see cref="Id"/>, timestamps (<see cref="CreatedAt"/>, <see cref="UpdatedAt"/>), and
/// <see cref="IsArchived"/>. Persistence operations are delegated to the injected
/// <see cref="IRepository{TSelf}"/>. Entity equality is determined by type and <see cref="Id"/>,
/// not by value — two instances of the same type with the same <see cref="Id"/> are equal even if
/// other properties differ.
/// <para>
/// Thread safety: not thread-safe. Do not share mutable instances across threads.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The concrete entity type. Must implement <see cref="IDomainEntity"/>.</typeparam>
public abstract record DomainEntity<TSelf> : IDomainEntity<TSelf>
    where TSelf : class, IDomainEntity
{
    private readonly IRepository<TSelf> repository;

    /// <summary>
    /// Constructor for NEW entities.
    /// </summary>
    /// <remarks>
    /// Assigns a new <see cref="Guid"/> to <see cref="Id"/>, sets <see cref="CreatedAt"/> and
    /// <see cref="UpdatedAt"/> to <see cref="DateTimeOffset.UtcNow"/>, and sets
    /// <see cref="IsArchived"/> to <see langword="false"/>.
    /// </remarks>
    /// <param name="repository">The repository used for persistence operations.</param>
    protected DomainEntity(IRepository<TSelf> repository)
    {
        this.repository = repository;
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        IsArchived = false;
    }

    /// <summary>
    /// Constructor for RECONSTITUTED entities loaded from storage.
    /// </summary>
    /// <remarks>
    /// Copies identity and timestamps from <paramref name="existing"/>; the resulting instance
    /// reflects the persisted state rather than initialising new values.
    /// </remarks>
    /// <param name="existing">The raw data record returned from the storage layer.</param>
    /// <param name="repository">The repository used for persistence operations.</param>
    protected DomainEntity(IDomainEntityData existing, IRepository<TSelf> repository)
    {
        this.repository = repository;
        Id = existing.Id;
        CreatedAt = existing.CreatedAt;
        UpdatedAt = existing.UpdatedAt;
        IsArchived = existing.IsArchived;
    }

    /// <summary>
    /// Unique identifier of this entity; never <see cref="Guid.Empty"/>.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// UTC instant the entity was first persisted; always in the past.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// UTC instant of the most recent write to this entity; always &gt;= <see cref="CreatedAt"/>.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// <see langword="true"/> when the entity has been soft-deleted.
    /// </summary>
    /// <remarks>
    /// Archived entities may still be resolved by <see cref="Id"/> but are excluded from list
    /// and paged queries.
    /// </remarks>
    public bool IsArchived { get; }

    /// <summary>
    /// Validates <see cref="Id"/>, <see cref="CreatedAt"/>, <see cref="UpdatedAt"/>, and
    /// <see cref="IsArchived"/> using the standard domain rules.
    /// </summary>
    /// <remarks>
    /// Derived classes should call <c>base.Validate</c> and then <c>yield return</c> their own
    /// rules so the full contract is always enforced.
    /// </remarks>
    /// <param name="validationContext">The validation context supplied by the framework.</param>
    /// <returns>
    /// A sequence of <see cref="ValidationResult"/> entries; <see cref="ValidationResult.Success"/>
    /// entries indicate passing checks.
    /// </returns>
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotDefault(Id);
        yield return Validation.InPast(CreatedAt);
        yield return Validation.InPast(UpdatedAt);
        yield return Validation.NotBefore(UpdatedAt, CreatedAt);
    }

    /// <summary>
    /// Fetches the latest persisted state of this entity from the repository.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The freshly loaded entity instance.</returns>
    /// <exception cref="Exceptions.EntityNotFoundException">
    /// The entity no longer exists in the repository.
    /// </exception>
    public Task<TSelf> ReloadAsync(CancellationToken cancellationToken = default)
        => repository.GetAsync(Id, cancellationToken);

    /// <summary>
    /// Persists this entity to the repository as a new record.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as returned by the repository.</returns>
    /// <exception cref="Exceptions.EntityAlreadyExistsException">
    /// An entity with the same <see cref="Id"/> already exists.
    /// </exception>
    public Task<TSelf> AddAsync(CancellationToken cancellationToken = default)
        => repository.AddAsync(this.As<TSelf>(), cancellationToken);

    /// <summary>
    /// Persists the current state of this entity to the repository.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The updated entity, as returned by the repository.</returns>
    /// <exception cref="Exceptions.EntityNotFoundException">
    /// The entity no longer exists in the repository.
    /// </exception>
    public Task<TSelf> UpdateAsync(CancellationToken cancellationToken = default)
        => repository.UpdateAsync(this.As<TSelf>(), cancellationToken);

    /// <summary>
    /// Validates <paramref name="updated"/> and then persists it via <see cref="UpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Intended for use by derived classes that follow an immutable-style mutation pattern: create a
    /// modified copy, pass it to <c>ApplyAsync</c>, and return the persisted result.
    /// </remarks>
    /// <param name="updated">The modified entity instance to validate and persist.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as returned by the repository.</returns>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">
    /// <paramref name="updated"/> fails validation.
    /// </exception>
    /// <exception cref="Exceptions.EntityNotFoundException">
    /// The entity no longer exists in the repository.
    /// </exception>
    protected Task<TSelf> ApplyAsync(TSelf updated, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(updated, new ValidationContext(updated), validateAllProperties: true);
        return repository.UpdateAsync(updated, cancellationToken);
    }

    /// <summary>
    /// Adds or updates this entity in the repository; safe regardless of whether it currently exists.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as returned by the repository.</returns>
    public Task<TSelf> UpsertAsync(CancellationToken cancellationToken = default)
        => repository.UpsertAsync(this.As<TSelf>(), cancellationToken);

    /// <summary>
    /// Permanently deletes this entity from the repository.
    /// </summary>
    /// <remarks>
    /// This is a hard delete. For soft deletion, implement <see cref="IArchivable"/> and call
    /// <see cref="IArchivableRepository{T}.ArchiveAsync"/> instead.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => repository.RemoveAsync(Id, cancellationToken);

    /// <summary>
    /// Determines equality by runtime type and <see cref="Id"/>.
    /// </summary>
    /// <remarks>
    /// Two entities of the same concrete type with the same <see cref="Id"/> are considered equal,
    /// regardless of any other property values.
    /// </remarks>
    /// <param name="other">The other entity to compare, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="other"/> is non-null, has the same runtime type,
    /// and has the same <see cref="Id"/>; otherwise <see langword="false"/>.
    /// </returns>
    public virtual bool Equals(DomainEntity<TSelf>? other)
        => other is not null && EqualityContract == other.EqualityContract && Id == other.Id;

    /// <summary>
    /// Returns a hash code consistent with <see cref="Equals(DomainEntity{TSelf}?)"/>.
    /// </summary>
    /// <remarks>
    /// Combines the runtime type and <see cref="Id"/> so that equal entities hash to the same bucket.
    /// </remarks>
    /// <returns>A hash code derived from the entity's type and <see cref="Id"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Id);
}
