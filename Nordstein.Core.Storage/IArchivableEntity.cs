namespace Nordstein.Core.Storage;

/// <summary>
/// Opt-in marker for stored entities that support soft-delete (archive). The mapped
/// <see cref="IsArchived"/> column backs <c>IDomainEntityData.IsArchived</c>; only entities that
/// implement this interface get the column. See <c>Nordstein.Core.Domain.IArchivable</c> for the
/// domain-side contract and <see cref="ArchivableRepository{TDomainEntity,TStoredEntity}"/> for the
/// repository behaviour.
/// </summary>
public interface IArchivableEntity
{
    bool IsArchived { get; init; }
}
