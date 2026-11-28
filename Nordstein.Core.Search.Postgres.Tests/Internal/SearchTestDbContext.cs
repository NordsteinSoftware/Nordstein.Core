using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Storage;

namespace Nordstein.Core.Search.Postgres.Tests.Internal;

/// <summary>
/// A context with no entities: the search objects live outside any EF model.
/// </summary>
internal sealed class SearchTestDbContext : NordsteinDbContext
{
    public SearchTestDbContext(IEnumerable<IModelConfiguration> configurations, DbContextOptions<SearchTestDbContext> options)
        : base(configurations, options)
    {
    }
}
