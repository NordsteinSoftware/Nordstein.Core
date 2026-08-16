namespace Nordstein.Core.Domain.Paging;

/// <summary>
/// A page of items together with paging metadata; an immutable value object.
/// </summary>
/// <typeparam name="T">The type of item in the page.</typeparam>
/// <param name="Items">The items on the current page.</param>
/// <param name="Total">The total number of matching entities across all pages.</param>
/// <param name="Page">The current 1-based page number.</param>
/// <param name="PageSize">The requested page size used to compute this result.</param>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    /// <summary>
    /// Projects each item in <see cref="Items"/> with <paramref name="selector"/> and returns a
    /// new <see cref="PagedResult{TOut}"/> that preserves <see cref="Total"/>, <see cref="Page"/>,
    /// and <see cref="PageSize"/>.
    /// </summary>
    /// <typeparam name="TOut">The type to project each item into.</typeparam>
    /// <param name="selector">The projection function applied to each item.</param>
    /// <returns>
    /// A new <see cref="PagedResult{TOut}"/> with the projected items and the same paging metadata.
    /// </returns>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
        => new(Items.Select(selector).ToArray(), Total, Page, PageSize);
}
