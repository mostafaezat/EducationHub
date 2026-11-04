namespace EducationHub.Shared.Common.Models;

/// <summary>
/// Immutable server-side page of results. Returned by every list endpoint so the
/// frontend never has to load a whole table (spec section 51).
/// </summary>
/// <remarks>
/// This type is deliberately free of any async factory method. It lives in the Shared
/// kernel, which has no EF Core reference; the paging query is built by
/// <c>Application.Common.Extensions.PaginationExtensions.ToPaginatedListAsync</c>,
/// which calls EF Core's own <c>CountAsync</c>/<c>ToListAsync</c> directly.
/// </remarks>
public sealed class PaginatedList<T>
{
    public PaginatedList(IReadOnlyList<T> items, int totalItems, int pageNumber, int pageSize)
    {
        Items = items;
        TotalItems = totalItems;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    public int TotalItems { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public static PaginatedList<T> Empty(int pageNumber = 1, int pageSize = 20)
        => new([], 0, pageNumber, pageSize);
}
