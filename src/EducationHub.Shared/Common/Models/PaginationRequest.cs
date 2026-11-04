namespace EducationHub.Shared.Common.Models;

/// <summary>
/// Base contract for every list query. Clamps are enforced in the property setters so
/// a careless or malicious client cannot request page 0 or 100 000 rows.
/// </summary>
public class PaginationRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _pageNumber = 1;
    private int _pageSize = DefaultPageSize;
    private string? _searchTerm;
    private string? _sortBy;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 or > MaxPageSize ? DefaultPageSize : value;
    }

    public string? SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? SortBy
    {
        get => _sortBy;
        set => _sortBy = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public SortOrder SortOrder { get; set; } = SortOrder.Asc;

    public int Skip => (PageNumber - 1) * PageSize;
}
