using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Contract for every stored (persistence) entity. Extends <see cref="IDomainEntityData"/> so a
/// stored entity can be passed directly as the <c>existing</c> argument of a domain
/// <c>CreateExisting</c> factory delegate, and <see cref="IValidatableObject"/> so the generic
/// repositories can validate it before a write.
/// </summary>
public interface IEntity : IDomainEntityData, IValidatableObject;
