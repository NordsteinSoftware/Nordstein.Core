using Autofac.Core;

namespace Nordstein.Core.Common.DependencyInjection;

/// <summary>
/// Reads the real failure out of Autofac's resolution wrapper.
/// </summary>
public static class ResolutionExceptionExtensions
{
    /// <summary>
    /// Returns the innermost exception reached by peeling every <see cref="DependencyResolutionException"/>
    /// that wraps another exception.
    /// </summary>
    /// <remarks>
    /// Autofac wraps a failure raised while it builds an object (a constructor, a factory delegate,
    /// an activation callback such as validation) in a <see cref="DependencyResolutionException"/>,
    /// once per resolution hop. Code that maps exceptions to an outcome (an exit code, a wire error)
    /// must look at the cause, not the wrapper. Only <see cref="DependencyResolutionException"/> is
    /// peeled: any other exception, even one with an inner exception, is the answer. A wrapper with
    /// no inner exception is returned as it is. Pure and thread-safe; allocates nothing.
    /// </remarks>
    /// <param name="exception">The exception to look through.</param>
    /// <returns>
    /// <paramref name="exception"/> itself unless it is a wrapper, else the first exception on its
    /// inner-exception chain that is not a wrapper around another exception.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static Exception UnwrapResolutionFailures(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception current = exception;
        while (current is DependencyResolutionException && current.InnerException is { } inner)
        {
            current = inner;
        }

        return current;
    }
}
