using Nordstein.Core.Common.Diagnostics;

namespace Nordstein.Core.Storage.Diagnostics;

/// <summary>
/// Implemented by a product's concrete context to hand the command timing (<see cref="RequestTimingOptionsExtensions.AddRequestTimings(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)"/>)
/// the accumulator of the unit of work that resolved the context, so each command lands in that unit's
/// <see cref="IRequestTimings"/>.
/// </summary>
/// <remarks>
/// A context takes the accumulator as an optional constructor argument
/// (<c>IRequestTimings? timings = null</c>), resolved from the same lifetime scope as the context; where none is
/// registered (design time, a migration tool) the property is <see langword="null"/> and commands are not timed.
/// </remarks>
public interface IRequestTimingSource
{
    /// <summary>
    /// The accumulator of the unit of work this context serves, or <see langword="null"/> for none.
    /// </summary>
    IRequestTimings? RequestTimings { get; }
}
