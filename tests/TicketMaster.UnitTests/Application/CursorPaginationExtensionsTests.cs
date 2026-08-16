#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using Xunit;

namespace TicketMaster.UnitTests.Application;

/// <summary>Exercises the cursor-page mechanics (peek/trim, next/previous emission, backward-then-reverse) against
/// an in-memory provider. The real EF-to-Postgres translation risk is covered separately by the integration test.</summary>
public class CursorPaginationExtensionsTests
{
    // Distinct, strictly increasing milliseconds so ordering is deterministic (no same-ms ties in this test).
    private static readonly DateTimeOffset Base = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<RowsContext> SeedAsync(int n)
    {
        var db = new RowsContext(new DbContextOptionsBuilder<RowsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        for (var i = 0; i < n; i++)
            db.Rows.Add(new Row { Id = Guid.CreateVersion7(Base.AddMilliseconds(i)), Value = i });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task First_page_has_no_previous_cursor_and_a_next_cursor_when_more_remain()
    {
        await using var db = await SeedAsync(7);

        var page = await db.Rows.OrderBy(r => r.Id)
            .ToCursorResultAsync(boundary: null, forward: true, count: 3, r => r.Id);

        page.Items.Select(r => r.Value).Should().Equal(0, 1, 2);
        page.PreviousCursor.Should().BeNull("no boundary was supplied — this is the first page");
        page.NextCursor.Should().NotBeNull();
        page.ItemsPerPage.Should().Be(3);
    }

    [Fact]
    public async Task Last_page_has_no_next_cursor_but_has_a_previous_cursor()
    {
        await using var db = await SeedAsync(7);
        // Max, not Min — the boundary must exclude row 3 itself (see CursorRequest.Create for the rationale).
        var boundary = UuidV7Cursor.MaxAtMillisecond(Base.AddMilliseconds(3).ToUnixTimeMilliseconds());

        var page = await db.Rows.Where(r => r.Id.CompareTo(boundary) > 0).OrderBy(r => r.Id)
            .ToCursorResultAsync(boundary, forward: true, count: 10, r => r.Id);

        page.Items.Select(r => r.Value).Should().Equal(4, 5, 6);
        page.NextCursor.Should().BeNull("the peek found no row past this page — nothing more to fetch");
        page.PreviousCursor.Should().NotBeNull("a boundary was supplied — there is a page to go back to");
    }

    [Fact]
    public async Task Paging_forward_covers_every_row_exactly_once()
    {
        await using var db = await SeedAsync(10);

        var seen = new System.Collections.Generic.List<int>();
        Guid? boundary = null;
        while (true)
        {
            var query = boundary is { } b
                ? db.Rows.Where(r => r.Id.CompareTo(b) > 0).OrderBy(r => r.Id)
                : db.Rows.OrderBy(r => r.Id);

            var page = await query.ToCursorResultAsync(boundary, forward: true, count: 4, r => r.Id);
            seen.AddRange(page.Items.Select(r => r.Value));

            if (page.NextCursor is null)
                break;

            UuidV7Cursor.TryDecodeCursor(page.NextCursor, out var ms, out var fwd).Should().BeTrue();
            fwd.Should().BeTrue();
            boundary = UuidV7Cursor.MaxAtMillisecond(ms);
        }

        seen.Should().Equal(Enumerable.Range(0, 10));
    }

    [Fact]
    public async Task Backward_page_reverses_back_to_ascending_order()
    {
        await using var db = await SeedAsync(7);
        // Min, not Max — the boundary must exclude row 5 itself (see CursorRequest.Create for the rationale).
        var boundary = UuidV7Cursor.MinAtMillisecond(Base.AddMilliseconds(5).ToUnixTimeMilliseconds());

        var page = await db.Rows.Where(r => r.Id.CompareTo(boundary) < 0).OrderByDescending(r => r.Id)
            .ToCursorResultAsync(boundary, forward: false, count: 3, r => r.Id);

        // Fetched DESC (closest-to-boundary first) then re-presented ascending, like every other page.
        page.Items.Select(r => r.Value).Should().Equal(2, 3, 4);
        page.NextCursor.Should().NotBeNull("a boundary was supplied — there is a page to go forward to");
        page.PreviousCursor.Should().NotBeNull("the peek found more rows further back");
    }

    [Fact]
    public async Task Count_zero_returns_an_empty_page_with_no_cursors()
    {
        await using var db = await SeedAsync(3);

        var page = await db.Rows.OrderBy(r => r.Id).ToCursorResultAsync(null, forward: true, count: 0, r => r.Id);

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        page.PreviousCursor.Should().BeNull();
    }

    private sealed class Row
    {
        public Guid Id { get; set; }
        public int Value { get; set; }
    }

    private sealed class RowsContext(DbContextOptions<RowsContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }
}
