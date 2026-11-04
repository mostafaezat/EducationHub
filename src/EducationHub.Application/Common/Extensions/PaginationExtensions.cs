using System.Linq.Expressions;
using System.Reflection;
using EducationHub.Shared.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Common.Extensions;

/// <summary>
/// Server-side paging, search and sorting shared by every list query.
/// </summary>
/// <remarks>
/// The whole point of <see cref="ToPaginatedListAsync{T}"/> is that it forces
/// <c>await</c> at the materialization boundary. Every list handler routes through it,
/// which turns "someone forgot to page a query and pulled 200 000 rows into memory" from
/// a code-review catch into a compile-time impossibility.
/// </remarks>
public static class PaginationExtensions
{
    public static async Task<PaginatedList<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> query,
        PaginationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        // Search and sort run BEFORE Skip/Take, so ordering sees the whole filtered set.
        var ordered = query
            .ApplySearch(request.SearchTerm)
            .ApplySort(request.SortBy, request.SortOrder);

        var totalItems = await ordered.CountAsync(cancellationToken).ConfigureAwait(false);

        if (totalItems == 0)
        {
            return PaginatedList<T>.Empty(request.PageNumber, request.PageSize);
        }

        // Skip past the end yields an empty page rather than throwing, which is what the
        // frontend needs when a filter shrinks the list under the current cursor.
        var items = await ordered
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PaginatedList<T>(items, totalItems, request.PageNumber, request.PageSize);
    }

    /// <summary>
    /// Multi-word search: every whitespace-separated term must match at least one string
    /// property (AND of terms, OR of columns).
    /// </summary>
    /// <remarks>
    /// The AND/OR shape is deliberate. Naive substring matching would rank a row containing
    /// <c>"Ali Hassan"</c> above a row containing <c>"Ali"</c> for the term "ali", and
    /// matching the whole phrase would ignore rows where the words are in another order.
    /// Matching every term independently is the behaviour users expect from a list filter.
    /// </remarks>
    public static IQueryable<T> ApplySearch<T>(this IQueryable<T> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        var properties = SearchableStringProperties(typeof(T));

        if (properties.Length == 0)
        {
            return query;
        }

        var terms = searchTerm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static term => term.ToLowerInvariant())
            .ToArray();

        foreach (var term in terms)
        {
            query = query.Where(BuildAnyPropertyMatch<T>(properties, term));
        }

        return query;
    }

    /// <summary>
    /// Orders by a client-named property.
    /// </summary>
    /// <remarks>
    /// The name is resolved through reflection into a <c>memberAccess</c> expression tree,
    /// so it is never concatenated into SQL and cannot become an injection point. An
    /// unknown or non-orderable name is ignored and the query is returned unchanged: a bad
    /// sort must not become a 500.
    /// </remarks>
    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, string? sortBy, SortOrder sortOrder)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return query;
        }

        var property = FindOrderableProperty(typeof(T), sortBy);

        if (property is null)
        {
            return query;
        }

        var descending = sortOrder == SortOrder.Desc;
        var keySelector = BuildObjectKeySelector<T>(property.Name);

        // Without a stable tie-break, two rows sharing the sort value can swap positions
        // between requests, so one row is skipped and another appears twice. Ordering by the
        // primary key second makes pagination deterministic.
        var keyName = FindPrimaryKeyName(typeof(T));

        if (keyName is null || keyName == property.Name)
        {
            return descending
                ? query.OrderByDescending(keySelector)
                : query.OrderBy(keySelector);
        }

        var tieBreaker = BuildObjectKeySelector<T>(keyName);

        return descending
            ? query.OrderByDescending(keySelector).ThenByDescending(tieBreaker)
            : query.OrderBy(keySelector).ThenBy(tieBreaker);
    }

    /// <summary>
    /// Builds <c>T -&gt; object?</c> so one generic helper can serve every sortable
    /// property type. EF Core unwraps the conversion and emits a properly typed ORDER BY.
    /// </summary>
    private static Expression<Func<T, object?>> BuildObjectKeySelector<T>(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var access = Expression.Property(parameter, propertyName);

        return Expression.Lambda<Func<T, object?>>(
            Expression.Convert(access, typeof(object)),
            parameter);
    }

    private static Expression<Func<T, bool>> BuildAnyPropertyMatch<T>(PropertyInfo[] properties, string term)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;

        foreach (var property in properties)
        {
            Expression access = Expression.Property(parameter, property);

            // string (non-nullable) is a compile-time promise only; the database can still
            // hold NULL, and Contains on null would throw inside the provider.
            access = Expression.Coalesce(access, Expression.Constant(string.Empty, typeof(string)));

            var contains = Expression.Call(
                access,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                Expression.Constant(term));

            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        return Expression.Lambda<Func<T, bool>>(body!, parameter);
    }

    private static PropertyInfo[] SearchableStringProperties(Type entityType)
        => [.. entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.PropertyType == typeof(string) && p.CanRead)];

    private static PropertyInfo? FindOrderableProperty(Type entityType, string name)
        => entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                && p.CanRead
                && IsOrderable(p.PropertyType));

    private static bool IsOrderable(Type type)
    {
        if (type == typeof(string))
        {
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(type);

        // Value types, nullable value types and enums are all orderable; reference types
        // other than string are excluded because no database sorts them consistently.
        return underlying is not null || type.IsValueType;
    }

    private static string? FindPrimaryKeyName(Type entityType)
        => entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(static p =>
                string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)
                && p.PropertyType == typeof(Guid)
                && p.CanRead)?
            .Name;
}
