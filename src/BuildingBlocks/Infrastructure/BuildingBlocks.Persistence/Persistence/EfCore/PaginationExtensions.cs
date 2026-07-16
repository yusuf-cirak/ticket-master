using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Pagination;

namespace BuildingBlocks.Persistence.EfCore;

public static class PaginationExtensions
{
    /// <summary>
    /// Materializes one page of a query plus its total count into a <see cref="PagedResult{T}"/>, clamping the page
    /// request to safe bounds. Order the query first — paging an unordered query is non-deterministic.
    /// </summary>
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
