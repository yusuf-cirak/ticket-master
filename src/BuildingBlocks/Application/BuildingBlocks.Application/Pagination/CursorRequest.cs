#nullable enable
using BuildingBlocks.Application.Http;

namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// An RFC 9865 cursor-pagination request read from the current request's query string (<c>cursor</c>/<c>count</c>),
/// mirroring <see cref="PageRequest"/>'s always-succeeding shape: a missing, unparseable, or malformed value falls
/// back to its default instead of failing the request — there is no invalid-cursor/invalid-count error path here,
/// only defaults. <see cref="Boundary"/> is the boundary id already decoded from the opaque cursor, carried as a
/// string so this record has no hard dependency on <see cref="Guid"/>; parse it (<c>Guid.Parse</c>) when building
/// the query.
/// </summary>
public sealed record CursorRequest(int Count, string? Boundary, bool Forward)
{
    public const int DefaultCount = 20;
    public const int MaxCount = 200;

    public static CursorRequest FromQuery()
    {
        var count = GetFromQuery("count") is { } countText && int.TryParse(countText, out var parsed)
            ? Math.Clamp(parsed, 0, MaxCount)
            : DefaultCount;

        var (boundary, forward) = GetFromQueryAsGuid7("cursor");
        return new CursorRequest(count, boundary, forward);
    }

    /// <summary>Raw value of <paramref name="key"/> from the current request's query string, or <c>null</c> if
    /// absent/empty/there is no current request.</summary>
    public static string? GetFromQuery(string key)
    {
        var value = ThreadHttpContextAccessor.Current?.Request.Query[key].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Reads <paramref name="key"/> and decodes it as an opaque UUIDv7 cursor, returning the range-scan
    /// boundary (already adjusted via <see cref="UuidV7Cursor.MinAtMillisecond"/>/<see cref="UuidV7Cursor.MaxAtMillisecond"/>
    /// to exclude the edge row that produced it) and its direction. A missing or malformed cursor yields
    /// <c>(null, true)</c> — the first page, forward.</summary>
    public static (string? Boundary, bool Forward) GetFromQueryAsGuid7(string key)
    {
        var cursor = GetFromQuery(key);
        if (cursor is null || !UuidV7Cursor.TryDecodeCursor(cursor, out var unixMs, out var forward))
            return (null, true);

        var boundary = forward ? UuidV7Cursor.MaxAtMillisecond(unixMs) : UuidV7Cursor.MinAtMillisecond(unixMs);
        return (boundary.ToString(), forward);
    }
}
