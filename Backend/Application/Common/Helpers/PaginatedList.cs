namespace CommonService.Application.Common.Helpers;

/// <summary>
/// Reusable paginated collection model.
/// </summary>
public class PaginatedList<T>
{
    public IReadOnlyCollection<T> Items { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages { get; }

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PaginatedList(IReadOnlyCollection<T> items, int count, int pageNumber, int pageSize)
    {
        PageNumber = pageNumber < 1 ? 1 : pageNumber;
        PageSize = pageSize < 1 ? 10 : pageSize;
        TotalCount = count < 0 ? 0 : count;
        TotalPages = PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
        Items = items ?? Array.Empty<T>();
    }

    public static PaginatedList<T> Create(IEnumerable<T> source, int pageNumber, int pageSize)
    {
        var list = source?.ToList() ?? new List<T>();
        var count = list.Count;
        var p = pageNumber < 1 ? 1 : pageNumber;
        var s = pageSize < 1 ? 10 : pageSize;
        var items = list.Skip((p - 1) * s).Take(s).ToList();
        return new PaginatedList<T>(items, count, p, s);
    }
}
