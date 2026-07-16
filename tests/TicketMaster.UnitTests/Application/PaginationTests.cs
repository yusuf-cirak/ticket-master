using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using Xunit;

namespace TicketMaster.UnitTests.Application;

public class PaginationTests
{
    [Theory]
    [InlineData(0, 10, 1, 10, 0)]     // page clamped up to 1
    [InlineData(3, 10, 3, 10, 20)]    // skip = (3-1)*10
    [InlineData(1, 0, 1, 20, 0)]      // size clamped to default
    [InlineData(1, 5000, 1, 200, 0)]  // size clamped to max
    public void PageRequest_normalizes_and_computes_skip(int page, int size, int nPage, int nSize, int skip)
    {
        var request = new PageRequest(page, size);
        request.NormalizedPage.Should().Be(nPage);
        request.NormalizedPageSize.Should().Be(nSize);
        request.Skip.Should().Be(skip);
    }

    [Fact]
    public void PagedResult_computes_pages_and_navigation()
    {
        var result = new PagedResult<int>(AsyncEnumerable.Range(1,3), Page: 2, PageSize: 10, TotalCount: 25);
        result.TotalPages.Should().Be(3);
        result.HasPrevious.Should().BeTrue();
        result.HasNext.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedResultAsync_returns_requested_page_and_total()
    {
        await using var db = new NumbersContext(new DbContextOptionsBuilder<NumbersContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Numbers.AddRange(Enumerable.Range(1, 25).Select(i => new Number { Id = i }));
        await db.SaveChangesAsync();

        var page = await db.Numbers.OrderBy(n => n.Id).ToPagedResultAsync(new PageRequest(Page: 2, PageSize: 10));

        page.TotalCount.Should().Be(25);
        page.Page.Should().Be(2);

        var items = await page.Items.Select(s => s.Id).ToArrayAsync();
        items.Should().Equal(Enumerable.Range(11, 10));
    }

    private sealed class Number
    {
        public int Id { get; set; }
    }

    private sealed class NumbersContext(DbContextOptions<NumbersContext> options) : DbContext(options)
    {
        public DbSet<Number> Numbers => Set<Number>();
    }
}
