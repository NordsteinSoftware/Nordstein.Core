namespace Nordstein.Core.Domain.Paging;

/// <summary>
/// Static helpers for validating and computing pagination parameters.
/// </summary>
/// <remarks>
/// Used by repository implementations to normalise <c>page</c> and <c>pageSize</c> values before
/// issuing a query to the underlying store.
/// </remarks>
public static class Paging
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Clamps <paramref name="page"/> to &gt;= 1 and <paramref name="pageSize"/> to [1, 100].
    /// </summary>
    /// <param name="page">The raw 1-based page number supplied by the caller.</param>
    /// <param name="pageSize">The raw page size supplied by the caller.</param>
    /// <returns>
    /// A tuple containing the clamped page number and clamped page size.
    /// </returns>
    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>
    /// Computes the zero-based row offset for a SQL <c>OFFSET</c> / <c>SKIP</c> clause.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>(page - 1) * pageSize</c>, with both inputs clamped to their minimum
    /// values and the result clamped to <see cref="int.MaxValue"/> to prevent overflow.
    /// </remarks>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>The zero-based row offset to skip before returning results.</returns>
    public static int Offset(int page, int pageSize)
        => (int)Math.Min((long)(Math.Max(1, page) - 1) * Math.Max(1, pageSize), int.MaxValue);
}
