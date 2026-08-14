using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Base implementation of <see cref="IEntity"/>. Every persisted entity derives from this and so
/// carries the identity/timestamp trio the generic repositories and the
/// <c>UpdatedAt</c> optimistic-concurrency convention depend on.
/// </summary>
public abstract record Entity : IEntity
{
    /// <inheritdoc cref="IDomainEntityData" />
    public required Guid Id { get; init; }

    /// <inheritdoc cref="IDomainEntityData" />
    public required DateTimeOffset CreatedAt { get; init; }

    /// <inheritdoc cref="IDomainEntityData" />
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <inheritdoc />
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotDefault(Id);
        yield return Validation.NotDefault(CreatedAt);
        yield return Validation.InPast(CreatedAt);
        yield return Validation.NotDefault(UpdatedAt);
        yield return Validation.InPast(UpdatedAt);
    }
}
