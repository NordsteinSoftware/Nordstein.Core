namespace Nordstein.Core.Common.Collections;

/// <summary>
/// Order-independent equality and hash codes for sets and dictionaries, for value objects that hold a collection and
/// must not depend on the order or the instance they were handed. Each hash is consistent with its equality: two
/// collections that compare equal under a comparer have the same hash under that comparer. Pure and thread-safe as long
/// as the collections are not changed during the call.
/// </summary>
public static class CollectionEquality
{
    /// <summary>
    /// Whether <paramref name="left"/> and <paramref name="right"/> hold the same distinct elements, in any order and
    /// with any repetition (set semantics). A <c>null</c> element counts as one element.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="left">The first collection.</param>
    /// <param name="right">The second collection.</param>
    /// <param name="comparer">The element comparer; <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
    /// <returns><c>true</c> when both hold the same distinct elements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <c>null</c>.</exception>
    public static bool SetEqualTo<T>(this IEnumerable<T> left, IEnumerable<T> right, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Distinct(left, comparer).SetEquals(right);
    }

    /// <summary>
    /// A hash code of the distinct elements of <paramref name="source"/>, independent of their order and repetition, and
    /// consistent with <see cref="SetEqualTo{T}"/> under the same comparer.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The collection.</param>
    /// <param name="comparer">The element comparer; <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
    /// <returns>The hash code; 0 for an empty collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
    public static int SetHashCode<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        IEqualityComparer<T> elements = comparer ?? EqualityComparer<T>.Default;
        int hash = 0;
        foreach (T element in Distinct(source, comparer))
        {
            // Addition is commutative, so the order of the set does not matter.
            hash = unchecked(hash + (element is null ? 0 : elements.GetHashCode(element)));
        }

        return hash;
    }

    /// <summary>
    /// Whether <paramref name="left"/> and <paramref name="right"/> hold the same keys with equal values, in any order.
    /// Each key of <paramref name="left"/> is looked up in <paramref name="right"/> with <paramref name="right"/>'s own
    /// key comparer; values compare with <paramref name="valueComparer"/>, a <c>null</c> value equal only to <c>null</c>.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="left">The first dictionary.</param>
    /// <param name="right">The second dictionary.</param>
    /// <param name="valueComparer">The value comparer; <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
    /// <returns><c>true</c> when both hold the same entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <c>null</c>.</exception>
    public static bool DictionaryEqualTo<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> left, IReadOnlyDictionary<TKey, TValue> right, IEqualityComparer<TValue>? valueComparer = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        IEqualityComparer<TValue> values = valueComparer ?? EqualityComparer<TValue>.Default;
        foreach ((TKey key, TValue value) in left)
        {
            if (!right.TryGetValue(key, out TValue? other) || !values.Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A hash code of the entries of <paramref name="source"/>, independent of their order, and consistent with
    /// <see cref="DictionaryEqualTo{TKey, TValue}"/> when both dictionaries use the same key comparer and the same
    /// <paramref name="valueComparer"/>.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="source">The dictionary.</param>
    /// <param name="valueComparer">The value comparer; <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
    /// <param name="keyComparer">The key comparer the dictionary uses; <see cref="EqualityComparer{T}.Default"/> when <c>null</c>.</param>
    /// <returns>The hash code; 0 for an empty dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
    public static int DictionaryHashCode<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> source, IEqualityComparer<TValue>? valueComparer = null, IEqualityComparer<TKey>? keyComparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        IEqualityComparer<TKey> keys = keyComparer ?? EqualityComparer<TKey>.Default;
        IEqualityComparer<TValue> values = valueComparer ?? EqualityComparer<TValue>.Default;
        int hash = 0;
        foreach ((TKey key, TValue value) in source)
        {
            int keyHash = key is null ? 0 : keys.GetHashCode(key);
            int valueHash = value is null ? 0 : values.GetHashCode(value);
            hash = unchecked(hash + HashCode.Combine(keyHash, valueHash));
        }

        return hash;
    }

    private static HashSet<T> Distinct<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer) => new(source, comparer);
}
