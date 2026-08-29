using System.Globalization;
using BuildingBlocks.Application.Http;

namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// A normalized page request. Callers may pass anything; <see cref="NormalizedPage"/>/<see cref="NormalizedPageSize"/>
/// clamp to safe bounds (page ≥ 1, size in 1..<see cref="MaxPageSize"/>) so a hostile or empty query can't ask for an
/// unbounded result set.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;

    /// <summary>Reads <c>page</c>/<c>pageSize</c> straight from the current request's query string; the default page
    /// outside a request (or when absent). Lets a paged query carry no PageRequest — <c>ToPagedResultAsync()</c> calls this.</summary>
    public static PageRequest FromCurrentRequest()
    {
        var query = ThreadHttpContextAccessor.Current?.Request.Query;
        if (query is null)
            return new PageRequest();

        var page = int.TryParse(query["page"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;
        var pageSize = int.TryParse(query["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : DefaultPageSize;
        return new PageRequest(page, pageSize);
    }
}
