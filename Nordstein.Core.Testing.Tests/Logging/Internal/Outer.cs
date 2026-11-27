namespace Nordstein.Core.Testing.Tests.Logging.Internal;

/// <summary>A generic type with a nested type, for the category-name shapes.</summary>
/// <typeparam name="T">Any type.</typeparam>
internal static class Outer<T>
{
    /// <summary>A type nested in a generic type: generic without a backtick in its own name.</summary>
    internal sealed class Inner
    {
        public override string ToString() => typeof(T).Name;
    }
}
