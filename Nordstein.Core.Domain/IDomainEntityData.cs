namespace Nordstein.Core.Domain;

/// <summary>
/// Data common to all persistent domain entities.
/// </summary>
public interface IDomainEntityData
{
    Guid Id { get; }

    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }

    bool IsArchived => false;
}
