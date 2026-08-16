#nullable enable
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Application.Http;
using BuildingBlocks.Application.Pagination;
using Xunit;

namespace TicketMaster.UnitTests.Pagination;

[Collection(ThreadHttpContextAccessorCollection.Name)]
public class CursorRequestTests
{
    [Fact]
    public void Create_with_no_query_string_succeeds_as_the_first_page()
    {
        SetQuery("");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeTrue();
        result.Value.Count.Should().Be(CursorRequest.DefaultCount);
        result.Value.Boundary.Should().BeNull();
        result.Value.Forward.Should().BeTrue();
    }

    [Fact]
    public void Create_outside_a_request_falls_back_to_the_first_page()
    {
        ThreadHttpContextAccessor.Configure(new StubAccessor(null));

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeTrue();
        result.Value.Count.Should().Be(CursorRequest.DefaultCount);
        result.Value.Boundary.Should().BeNull();
    }

    [Fact]
    public void Create_clamps_a_negative_count_to_zero_without_erroring()
    {
        SetQuery("?count=-5");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeTrue("RFC 9865: a negative count SHALL be interpreted as 0, not rejected");
        result.Value.Count.Should().Be(0);
    }

    [Fact]
    public void Create_rejects_an_unparseable_count()
    {
        SetQuery("?count=not-a-number");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("pagination.invalid_count");
        result.Error.Status.Should().Be(400);
    }

    [Fact]
    public void Create_rejects_a_count_over_the_maximum()
    {
        SetQuery($"?count={CursorRequest.MaxCount + 1}");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("pagination.invalid_count");
    }

    [Fact]
    public void Create_rejects_a_malformed_cursor()
    {
        SetQuery("?cursor=not-a-real-cursor!!");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("pagination.invalid_cursor");
        result.Error.Status.Should().Be(400);
    }

    [Fact]
    public void Create_decodes_a_well_formed_forward_cursor_into_its_boundary()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetQuery($"?cursor={UuidV7Cursor.EncodeCursor(unixMs, forward: true)}");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeTrue();
        result.Value.Forward.Should().BeTrue();
        // Max, not Min: the boundary must exclude the edge row that produced the cursor (see CursorRequest.Create).
        result.Value.Boundary.Should().Be(UuidV7Cursor.MaxAtMillisecond(unixMs));
    }

    [Fact]
    public void Create_decodes_a_well_formed_backward_cursor_into_its_boundary()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetQuery($"?cursor={UuidV7Cursor.EncodeCursor(unixMs, forward: false)}");

        var result = CursorRequest.Create();

        result.IsSuccess.Should().BeTrue();
        result.Value.Forward.Should().BeFalse();
        result.Value.Boundary.Should().Be(UuidV7Cursor.MinAtMillisecond(unixMs));
    }

    private static void SetQuery(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        ThreadHttpContextAccessor.Configure(new StubAccessor(context));
    }

    private sealed class StubAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }
}
