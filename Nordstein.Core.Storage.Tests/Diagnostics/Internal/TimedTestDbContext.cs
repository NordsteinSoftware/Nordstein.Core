using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Diagnostics;
using Nordstein.Core.Storage.Diagnostics;

namespace Nordstein.Core.Storage.Tests.Diagnostics.Internal;

/// <summary>
/// A context that names its unit of work's accumulator, the way a product's concrete context opts in.
/// </summary>
internal sealed class TimedTestDbContext : NordsteinDbContext, IRequestTimingSource
{
    public TimedTestDbContext(DbContextOptions<TimedTestDbContext> options, IRequestTimings? timings = null)
        : base([], options)
    {
        RequestTimings = timings;
    }

    public IRequestTimings? RequestTimings { get; }
}

/// <summary>
/// A context that does not opt in.
/// </summary>
internal sealed class UntimedTestDbContext : DbContext
{
    public UntimedTestDbContext(DbContextOptions<UntimedTestDbContext> options)
        : base(options)
    {
    }
}
