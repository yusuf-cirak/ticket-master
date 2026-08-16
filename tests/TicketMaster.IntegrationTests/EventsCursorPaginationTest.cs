#nullable enable
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// End-to-end HTTP exercise of the RFC 9865 cursor-pagination endpoint (<c>GET /api/events/cursor</c>) against the
/// shared spine's real Postgres. This is also the concrete proof that <c>e.Id.CompareTo(EventId.From(boundary))</c>
/// actually translates to SQL (not client-evaluated) — a documented risk in <c>ListEventsByCursorQuery</c> that only
/// a real database run can settle.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class EventsCursorPaginationTest(SpineFixture fixture)
{
    private const string TenantA = "33333333-3333-3333-3333-333333333333";
    private const string TenantB = "44444444-4444-4444-4444-444444444444";

    private sealed record ScheduleBody(string Name, string Venue, DateTimeOffset StartsOn, int Capacity);
    private sealed record CreatedResponse(Guid Id);
    private sealed record CursorItem(Guid Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity, bool IsLargeVenue);
    private sealed record CursorView(List<CursorItem> Items, string? NextCursor, string? PreviousCursor, int ItemsPerPage);

    private HttpRequestMessage Request(HttpMethod method, string url, string tenant, string? bearerToken = null, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenant);
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task Paging_forward_covers_every_scheduled_event_once_and_backward_reproduces_the_prior_page()
    {
        await fixture.ResetRedisAsync();
        // Authenticated tier (multiplier 1.0 = 20/5s) rather than anon (0.25 = 5/5s) — this test needs headroom for
        // 10 schedules + several cursor pages within one burst window.
        var token = fixture.MintToken();

        var scheduledIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var response = await fixture.Client.SendAsync(Request(
                HttpMethod.Post, "/api/events", TenantA, token,
                new ScheduleBody($"Cursor Show {i}", "Cursor Hall", DateTimeOffset.UtcNow.AddDays(i + 1), 100 + i),
                Guid.NewGuid().ToString()));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            scheduledIds.Add((await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        }

        // Page forward, 4 at a time (10 = 4 + 4 + 2), collecting every id and every page for the backward check below.
        var pages = new List<CursorView>();
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? "/api/events/cursor?count=4" : $"/api/events/cursor?count=4&cursor={Uri.EscapeDataString(cursor)}";
            var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, url, TenantA, token));
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = (await response.Content.ReadFromJsonAsync<CursorView>())!;
            pages.Add(page);
            seen.AddRange(page.Items.Select(i => i.Id).ToArray());
            cursor = page.NextCursor;
        } while (cursor is not null);

        seen.Should().BeEquivalentTo(scheduledIds, "every scheduled event must appear exactly once across all pages");
        pages.Should().HaveCount(3, "10 items at 4 per page is 3 pages (4, 4, 2)");
        pages[0].PreviousCursor.Should().BeNull("RFC 9865: previousCursor must not appear on the first page");
        pages[^1].NextCursor.Should().BeNull("the last page has nothing more to fetch");
        pages[1].PreviousCursor.Should().NotBeNull("a middle page was reached via a cursor, so it can go back");

        // Walk backward from the last page's previousCursor — must reproduce page[1] (the page right before it).
        var backUrl = $"/api/events/cursor?count=4&cursor={Uri.EscapeDataString(pages[^1].PreviousCursor!)}";
        var backResponse = await fixture.Client.SendAsync(Request(HttpMethod.Get, backUrl, TenantA, token));
        var backPage = (await backResponse.Content.ReadFromJsonAsync<CursorView>())!;

        backPage.Items.Select(i => i.Id).ToArray().Should().Equal(pages[1].Items.Select(i => i.Id).ToArray(),
            "paging backward from the last page's previousCursor must reproduce the middle page exactly");
    }

    [Fact]
    public async Task A_malformed_cursor_returns_400_invalid_cursor()
    {
        await fixture.ResetRedisAsync();

        var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/api/events/cursor?cursor=not-a-real-cursor!!", TenantA));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("pagination.invalid_cursor");
    }

    [Fact]
    public async Task A_count_over_the_maximum_returns_400_invalid_count()
    {
        await fixture.ResetRedisAsync();

        var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/api/events/cursor?count=99999", TenantA));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("pagination.invalid_count");
    }

    [Fact]
    public async Task A_tenant_cannot_see_another_tenants_events_via_cursor_pagination()
    {
        await fixture.ResetRedisAsync();
        var created = await fixture.Client.SendAsync(Request(
            HttpMethod.Post, "/api/events", TenantA, body: new ScheduleBody("A-only Cursor Show", "Hall A", DateTimeOffset.UtcNow.AddDays(3), 50),
            idempotencyKey: Guid.NewGuid().ToString()));
        var aEventId = (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        var bResponse = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/api/events/cursor?count=200", TenantB));
        var bPage = (await bResponse.Content.ReadFromJsonAsync<CursorView>())!;

        bPage.Items.Should().NotContain(i => i.Id == aEventId, "tenant B is isolated from tenant A's data (the same global query filter cursor pagination reuses)");
    }
}
