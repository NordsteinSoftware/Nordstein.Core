using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nordstein.Core.Common.Diagnostics;
using Nordstein.Core.Storage.Diagnostics.Internal;

namespace Nordstein.Core.Storage.Diagnostics;

/// <summary>
/// Opts a context's options in to command timing: every database command the context runs adds its duration
/// and one count, and every data reader its reading time, under <see cref="RequestTimingNames.Database"/> in the
/// <see cref="IRequestTimings"/> its <see cref="IRequestTimingSource"/> names.
/// </summary>
/// <remarks>
/// <para>
/// The interceptor is a relational <see cref="Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor"/>:
/// it sees the commands EF Core runs through a relational provider, not hand-written ADO.NET commands on the
/// context's connection, and a non-relational provider (the in-memory one) never calls it. It records names,
/// durations and counts only, never command text or parameters. A context that does not implement
/// <see cref="IRequestTimingSource"/>, or whose accumulator is <see langword="null"/>, is skipped.
/// </para>
/// <para>
/// Stateless: one interceptor serves every context the options build. Adding it twice to the same builder adds
/// it once, so no command is counted twice.
/// </para>
/// </remarks>
public static class RequestTimingOptionsExtensions
{
    /// <summary>
    /// Adds the command timing interceptor to <paramref name="builder"/>, unless it is already there.
    /// </summary>
    /// <param name="builder">The context options being built.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder AddRequestTimings(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        bool present = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<CommandTimingInterceptor>().Any() == true;
        return present ? builder : builder.AddInterceptors(new CommandTimingInterceptor());
    }

    /// <summary>
    /// Adds the command timing interceptor to <paramref name="builder"/>, unless it is already there.
    /// </summary>
    /// <typeparam name="TContext">The product's concrete context.</typeparam>
    /// <param name="builder">The context options being built.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder<TContext> AddRequestTimings<TContext>(this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = ((DbContextOptionsBuilder)builder).AddRequestTimings();
        return builder;
    }
}
