namespace Wazap.Application.Dtos;

public class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int total, int page, int pageSize)
    {
        Items = items;
        Total = total;
        Page = page;
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling(total / (double)pageSize);
    }

    public IReadOnlyList<T> Items { get; }
    public int Total { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
}
