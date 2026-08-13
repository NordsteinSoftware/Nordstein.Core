using System.ComponentModel.DataAnnotations;

namespace Nordstein.Core.Domain;

/// <summary>
/// Base interface for domain objects that support validation.
/// </summary>
public interface IDomainObject : IValidatableObject;
