using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Infrastructure.Grid;

/// <summary>
/// Paging / sorting / search parameters sent by wwwroot/js/grid.js:
///   ?page=1&amp;pageSize=20&amp;sort=companyName&amp;dir=asc&amp;search=oq
/// Filtering is done by each controller with typed parameters (safe).
/// </summary>
public class GridRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public string? Search { get; set; }

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
    public string? SearchTerm => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

public sealed record GridResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class GridExtensions
{
    /// <summary>
    /// Sorts by a property of the projected row type. The requested name is matched
    /// against the real properties of T (case-insensitive), so a client can never
    /// inject SQL or sort by an arbitrary expression.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, GridRequest request, string defaultSort, bool defaultDescending = false, string? tieBreaker = null)
    {
        var property = FindProperty<T>(request.Sort) ?? FindProperty<T>(defaultSort)
                       ?? throw new ArgumentException($"Unknown sort property '{defaultSort}' on {typeof(T).Name}");
        var descending = FindProperty<T>(request.Sort) != null ? request.Descending : defaultDescending;

        var ordered = OrderBy(query, property, descending, first: true);
        var tie = FindProperty<T>(tieBreaker);
        if (tie != null && tie != property)
            ordered = OrderBy(ordered, tie, descending, first: false);
        return ordered;
    }

    public static async Task<GridResult<T>> ToGridResultAsync<T>(this IQueryable<T> query, GridRequest request, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 5, 200);
        var total = await query.CountAsync(ct);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, lastPage);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new GridResult<T>(items, total, page, pageSize);
    }

    private static PropertyInfo? FindProperty<T>(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    private static IQueryable<T> OrderBy<T>(IQueryable<T> query, PropertyInfo property, bool descending, bool first)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var body = Expression.Property(parameter, property);
        var lambda = Expression.Lambda(body, parameter);
        var method = first
            ? (descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy))
            : (descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));
        var call = Expression.Call(typeof(Queryable), method, new[] { typeof(T), property.PropertyType }, query.Expression, Expression.Quote(lambda));
        return query.Provider.CreateQuery<T>(call);
    }
}
