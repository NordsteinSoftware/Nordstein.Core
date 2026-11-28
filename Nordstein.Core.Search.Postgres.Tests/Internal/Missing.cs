namespace Nordstein.Core.Search.Postgres.Tests.Internal;

/// <summary>
/// A null of a non-nullable reference type, for the tests of null guards. A fresh array's element
/// is null without a null-forgiving operator.
/// </summary>
internal static class Missing
{
    public static T Reference<T>()
        where T : class
        => (new T[1])[0];
}
