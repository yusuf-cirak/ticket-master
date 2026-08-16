#nullable enable
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Pagination;

namespace BuildingBlocks.Persistence.EfCore;

public static class CursorPaginationExtensions
{
    /// <summary>
    /// Materializes an RFC 9865 cursor page. The caller's query must already have the boundary filter and matching
    /// order applied (<c>id &gt; boundary</c> + <c>OrderBy(id)</c> for forward, <c>id &lt; boundary</c> +
    /// <c>OrderByDescending(id)</c> for backward) — this only peeks one row past <paramref name="count"/> to know
    /// whether there is more, trims it off, and builds <see cref="CursorResult{T}.NextCursor"/>/
    /// <see cref="CursorResult{T}.PreviousCursor"/> from the materialized page's edge rows.
    /// <para>
    /// <paramref name="boundary"/> is the ORIGINAL validated request's boundary (not the page's own edge) — it is
    /// only used to tell "first page" (<c>null</c>) from "navigated here via a cursor" (non-null), which decides
    /// whether the opposite-direction cursor should be emitted (RFC 9865: <c>previousCursor</c> must not appear on
    /// the first page).
    /// </para>
    /// </summary>
    public static async Task<CursorResult<T>> ToCursorResultAsync<T>(
        this IQueryable<T> orderedFilteredQuery,
        Guid? boundary,
        bool forward,
        int count,
        Func<T, Guid> idSelector,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return new CursorResult<T>([], null, null, 0);

        var rows = await orderedFilteredQuery.Take(count + 1).ToListAsync(cancellationToken);

        var hasMore = rows.Count > count;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1); // drop the peek row

        if (!forward)
            rows.Reverse(); // fetched DESC to walk backward from the boundary; re-present ascending like every page

        string? nextCursor = null;
        string? previousCursor = null;

        if (rows.Count > 0)
        {
            // Symmetric rule: there is more in whichever direction this page didn't just consume from the boundary,
            // and there is a way back in whichever direction it DID come from (boundary not null).
            var canGoNext = forward ? hasMore : boundary is not null;
            var canGoPrevious = forward ? boundary is not null : hasMore;

            if (canGoNext)
                nextCursor = UuidV7Cursor.EncodeCursor(UuidV7Cursor.ExtractMillisecond(idSelector(rows[^1])), forward: true);
            if (canGoPrevious)
                previousCursor = UuidV7Cursor.EncodeCursor(UuidV7Cursor.ExtractMillisecond(idSelector(rows[0])), forward: false);
        }

        return new CursorResult<T>(rows, nextCursor, previousCursor, rows.Count);
    }
}
