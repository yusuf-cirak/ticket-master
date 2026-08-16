#nullable enable

namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// One page of an RFC 9865 cursor-paginated result. Unlike <see cref="PagedResult{T}"/> (which streams an
/// <c>IAsyncEnumerable&lt;T&gt;</c> lazily), this materializes its items — determining <see cref="NextCursor"/>
/// needs peeking one row past the page, so the page can't stay a pure lazy stream.
/// <para>
/// <c>TotalResults</c> is deliberately absent: RFC 9865 allows omitting it when it can't be estimated cheaply, and
/// avoiding that COUNT query (on top of avoiding OFFSET) is the whole performance point of this pagination method.
/// </para>
/// </summary>
public sealed record CursorResult<T>(IReadOnlyList<T> Items, string? NextCursor, string? PreviousCursor, int ItemsPerPage);
