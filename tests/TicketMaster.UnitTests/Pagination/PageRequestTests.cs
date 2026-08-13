#nullable enable
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Application.Http;
using BuildingBlocks.Application.Pagination;
using Xunit;

namespace TicketMaster.UnitTests.Pagination;

public class PageRequestTests
{
    [Fact]
    public void FromCurrentRequest_reads_page_and_pageSize_from_the_query_string()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?page=3&pageSize=50");
        ThreadHttpContextAccessor.Configure(new StubAccessor(context));

        var page = PageRequest.FromCurrentRequest();

        page.Page.Should().Be(3);
        page.PageSize.Should().Be(50);
    }

    [Fact]
    public void FromCurrentRequest_falls_back_to_the_default_when_there_is_no_request()
    {
        ThreadHttpContextAccessor.Configure(new StubAccessor(null));

        var page = PageRequest.FromCurrentRequest();

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    private sealed class StubAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }
}
