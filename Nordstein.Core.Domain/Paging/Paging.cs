namespace Nordstein.Core.Domain.Paging;

public static class Paging
{
    private const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    public static int Offset(int page, int pageSize)
        => (int)Math.Min((long)(Math.Max(1, page) - 1) * Math.Max(1, pageSize), int.MaxValue);
}
