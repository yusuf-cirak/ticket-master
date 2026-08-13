using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Pagination;

namespace BuildingBlocks.Persistence.EfCore;

public static class PaginationExtensions
{
    /// <summary>
    /// Materializes one page of a query plus its total count into a <see cref="PagedResult{T}"/>, clamping the page
    /// request to safe bounds. Order the query first — paging an unordered query is non-deterministic.
    /// </summary>
    /// <summary>
    /// Pages using the current request's query string (<see cref="PageRequest.FromCurrentRequest"/>) — no
    /// <see cref="PageRequest"/> to thread through the query/handler. Outside an HTTP request this is the default page
    /// (1/20); a non-HTTP caller that needs a specific page uses the
    /// <see cref="ToPagedResultAsync{T}(IQueryable{T}, PageRequest, CancellationToken)"/> overload.
    /// </summary>
    public static Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.ToPagedResultAsync(PageRequest.FromCurrentRequest(), cancellationToken);

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var total = await query.LongCountAsync(cancellationToken);
        var items =  query
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .AsAsyncEnumerable();

        return new PagedResult<T>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }
}
