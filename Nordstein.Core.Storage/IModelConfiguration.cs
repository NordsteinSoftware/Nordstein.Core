using Microsoft.EntityFrameworkCore;

namespace Nordstein.Core.Storage;

/// <summary>
/// Applies a slice of EF Core model configuration. <see cref="NordsteinDbContext"/> resolves every
/// registered implementation and calls <see cref="CreateModel"/> during <c>OnModelCreating</c>.
/// </summary>
public interface IModelConfiguration
{
    /// <summary>Configures the model using the provided builder.</summary>
    void CreateModel(ModelBuilder builder);
}
