namespace Nordstein.Core.Common.Diagnostics;

/// <summary>
/// The names Core's own writers record under in <see cref="IRequestTimings"/>. A product keeps its names in its
/// own constants; pick ones that cannot collide with these.
/// </summary>
public static class RequestTimingNames
{
    /// <summary>
    /// Duration and count: every database command of the unit of work, a reader's reading time included. Written
    /// by the command interceptor of <c>Nordstein.Core.Storage</c> (<c>AddRequestTimings</c>).
    /// </summary>
    public const string Database = "db";
}
