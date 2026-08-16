#nullable enable
using BuildingBlocks.Application.Http;
using BuildingBlocks.Domain.Errors;
using YC.Monad;

namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// A parsed, ready-to-query RFC 9865 cursor-pagination request. <see cref="Create"/> reads <c>cursor</c>/<c>count</c>
/// from the current request's query string and applies the RFC's rules in one step — there is no unvalidated
/// intermediate value to construct and forget to check before using it. A missing count defaults to
/// <see cref="DefaultCount"/>; a negative count is clamped to 0 (RFC 9865's own exception — "a negative value SHALL
/// be interpreted as '0'"); everything else invalid (unparseable count, count above <see cref="MaxCount"/>, a
/// malformed cursor) fails with the matching 400 (<c>pagination.invalid_count</c> / <c>pagination.invalid_cursor</c>),
/// matching this repo's <c>Error.Create(code, message, status)</c> convention.
/// </summary>
public sealed record CursorRequest(int Count, Guid? Boundary, bool Forward)
{
    public const int DefaultCount = 20;
    public const int MaxCount = 200;

    public static Result<CursorRequest> Create()
    {
        var query = ThreadHttpContextAccessor.Current?.Request.Query;
        var cursor = query?["cursor"].ToString();
        var countText = query?["count"].ToString();
        cursor = string.IsNullOrEmpty(cursor) ? null : cursor;
        countText = string.IsNullOrEmpty(countText) ? null : countText;

        int count;
        if (countText is null)
        {
            count = DefaultCount;
        }
        else if (!int.TryParse(countText, out count))
        {
            return Error.Create("pagination.invalid_count", "Count must be a whole number.", 400);
        }
        else if (count < 0)
        {
            count = 0;
        }
        else if (count > MaxCount)
        {
            return Error.Create("pagination.invalid_count", $"Count must be between 0 and {MaxCount}.", 400);
        }

        if (cursor is null)
            return Result.Success(new CursorRequest(count, Boundary: null, Forward: true));

        if (!UuidV7Cursor.TryDecodeCursor(cursor, out var unixMs, out var forward))
            return Error.Create("pagination.invalid_cursor", "Cursor value is malformed.", 400);

        // The cursor's millisecond is always the id of the row that PRODUCED it (the page's edge row), so the
        // boundary must be the extreme that EXCLUDES that row: forward (id > boundary) needs the largest possible
        // id at that ms (Max), backward (id < boundary) needs the smallest (Min) — otherwise the edge row would
        // reappear on every single page transition, not just in a rare same-millisecond collision.
        var boundary = forward ? UuidV7Cursor.MaxAtMillisecond(unixMs) : UuidV7Cursor.MinAtMillisecond(unixMs);
        return Result.Success(new CursorRequest(count, boundary, forward));
    }
}
