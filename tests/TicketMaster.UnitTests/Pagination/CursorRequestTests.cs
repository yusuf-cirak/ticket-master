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
    public void FromQuery_with_no_query_string_defaults_to_the_first_page()
    {
        SetQuery("");

        var request = CursorRequest.FromQuery();

        request.Count.Should().Be(CursorRequest.DefaultCount);
        request.Boundary.Should().BeNull();
        request.Forward.Should().BeTrue();
    }

    [Fact]
    public void FromQuery_outside_a_request_falls_back_to_the_first_page()
    {
        ThreadHttpContextAccessor.Configure(new StubAccessor(null));

        var request = CursorRequest.FromQuery();

        request.Count.Should().Be(CursorRequest.DefaultCount);
        request.Boundary.Should().BeNull();
    }

    [Fact]
    public void FromQuery_clamps_a_negative_count_to_zero()
    {
        SetQuery("?count=-5");

        CursorRequest.FromQuery().Count.Should().Be(0);
    }

    [Fact]
    public void FromQuery_clamps_a_count_over_the_maximum()
    {
        SetQuery($"?count={CursorRequest.MaxCount + 1}");

        CursorRequest.FromQuery().Count.Should().Be(CursorRequest.MaxCount);
    }

    [Fact]
    public void FromQuery_defaults_an_unparseable_count_instead_of_failing()
    {
        SetQuery("?count=not-a-number");

        CursorRequest.FromQuery().Count.Should().Be(CursorRequest.DefaultCount);
    }

    [Fact]
    public void FromQuery_falls_back_to_the_first_page_on_a_malformed_cursor()
    {
        SetQuery("?cursor=not-a-real-cursor!!");

        var request = CursorRequest.FromQuery();

        request.Boundary.Should().BeNull();
        request.Forward.Should().BeTrue();
    }

    [Fact]
    public void FromQuery_decodes_a_well_formed_forward_cursor_into_its_boundary()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetQuery($"?cursor={UuidV7Cursor.EncodeCursor(unixMs, forward: true)}");

        var request = CursorRequest.FromQuery();

        request.Forward.Should().BeTrue();
        // Max, not Min: the boundary must exclude the edge row that produced the cursor.
        request.Boundary.Should().Be(UuidV7Cursor.MaxAtMillisecond(unixMs).ToString());
    }

    [Fact]
    public void FromQuery_decodes_a_well_formed_backward_cursor_into_its_boundary()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetQuery($"?cursor={UuidV7Cursor.EncodeCursor(unixMs, forward: false)}");

        var request = CursorRequest.FromQuery();

        request.Forward.Should().BeFalse();
        request.Boundary.Should().Be(UuidV7Cursor.MinAtMillisecond(unixMs).ToString());
    }

    [Fact]
    public void GetFromQuery_reads_the_raw_value_for_a_key()
    {
        SetQuery("?count=42");

        CursorRequest.GetFromQuery("count").Should().Be("42");
        CursorRequest.GetFromQuery("missing").Should().BeNull();
    }

    [Fact]
    public void GetFromQueryAsGuid7_decodes_the_cursor_at_the_given_key()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetQuery($"?cursor={UuidV7Cursor.EncodeCursor(unixMs, forward: true)}");

        var (boundary, forward) = CursorRequest.GetFromQueryAsGuid7("cursor");

        forward.Should().BeTrue();
        boundary.Should().Be(UuidV7Cursor.MaxAtMillisecond(unixMs).ToString());
    }

    [Fact]
    public void GetFromQueryAsGuid7_returns_null_for_a_missing_or_malformed_key()
    {
        SetQuery("?cursor=not-a-real-cursor!!");

        var (boundary, forward) = CursorRequest.GetFromQueryAsGuid7("cursor");

        boundary.Should().BeNull();
        forward.Should().BeTrue();
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
