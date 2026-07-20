#nullable enable
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// End-to-end HTTP exercise of the Events demo module against the shared spine's real Postgres. Every call carries an
/// <c>X-Tenant-Id</c> header (the header tenant resolver), so it also proves multi-tenant isolation: a tenant only ever
/// sees and mutates its own events, enforced by the global query filter + write interceptor — not by handler code.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class EventsEndpointTest(SpineFixture fixture)
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    private sealed record ScheduleBody(string Name, string Venue, DateTimeOffset StartsOn, int Capacity);
    private sealed record CreatedResponse(Guid Id);
    private sealed record EventView(Guid Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity);
    private sealed record ListItem(Guid Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity, bool IsLargeVenue);
    private sealed record PagedView(List<ListItem> Items, int Page, int PageSize, long TotalCount);

    private HttpRequestMessage Request(HttpMethod method, string url, string tenant, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenant);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<CreatedResponse> ScheduleAsync(string tenant, ScheduleBody body)
    {
        var response = await fixture.Client.SendAsync(Request(HttpMethod.Post, "/api/events", tenant, body, Guid.NewGuid().ToString()));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!;
    }

    [Fact]
    public async Task Schedule_then_get_and_list_roundtrips_within_the_tenant()
    {
        await fixture.ResetRedisAsync(); // clean rate-limit window (anon burst = 5/5s, shared)
        var created = await ScheduleAsync(TenantA, new ScheduleBody("Cup Final", "Grand Arena", DateTimeOffset.UtcNow.AddDays(30), 5000));
        created.Id.Should().NotBe(Guid.Empty);

        var get = await fixture.Client.SendAsync(Request(HttpMethod.Get, $"/api/events/{created.Id}", TenantA));
        var got = await get.Content.ReadFromJsonAsync<EventView>();
        got!.Name.Should().Be("Cup Final");
        got.Capacity.Should().Be(5000);

        var listResponse = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/api/events?page=1&pageSize=200", TenantA));
        var page = await listResponse.Content.ReadFromJsonAsync<PagedView>();
        page!.Items.Should().ContainSingle(e => e.Id == created.Id)
            .Which.IsLargeVenue.Should().BeTrue("capacity 5000 >= 1000, computed in SQL via [Projectable]");
    }

    [Fact]
    public async Task A_tenant_cannot_see_or_fetch_another_tenants_event()
    {
        await fixture.ResetRedisAsync(); // clean rate-limit window (anon burst = 5/5s, shared)
        // Tenant A schedules an event...
        var aEvent = await ScheduleAsync(TenantA, new ScheduleBody("A-only Show", "Hall A", DateTimeOffset.UtcNow.AddDays(5), 100));

        // ...tenant B must not see it in the list, nor fetch it by id (global query filter scopes both).
        var bList = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/api/events?page=1&pageSize=200", TenantB));
        var bPage = await bList.Content.ReadFromJsonAsync<PagedView>();
        bPage!.Items.Should().NotContain(e => e.Id == aEvent.Id, "tenant B is isolated from tenant A's data");

        var bGet = await fixture.Client.SendAsync(Request(HttpMethod.Get, $"/api/events/{aEvent.Id}", TenantB));
        bGet.StatusCode.Should().Be(HttpStatusCode.NotFound, "the row is invisible to another tenant, so it reads as not found");
    }

    [Fact]
    public async Task Scheduling_without_a_tenant_is_rejected()
    {
        await fixture.ResetRedisAsync(); // clean rate-limit window (anon burst = 5/5s, shared)
        // No X-Tenant-Id header and anonymous → no ambient tenant → the write interceptor refuses (fail closed).
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/events")
        {
            Content = JsonContent.Create(new ScheduleBody("No Tenant", "Nowhere", DateTimeOffset.UtcNow.AddDays(1), 10))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await fixture.Client.SendAsync(request);
        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Get_unknown_event_returns_404()
    {
        await fixture.ResetRedisAsync(); // clean rate-limit window (anon burst = 5/5s, shared)
        var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, $"/api/events/{Guid.NewGuid()}", TenantA));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_the_first_result()
    {
        await fixture.ResetRedisAsync(); // clean rate-limit window (anon burst = 5/5s, shared)
        var body = new ScheduleBody("Replay Show", "Replay Hall", DateTimeOffset.UtcNow.AddDays(10), 42);
        var key = Guid.NewGuid().ToString();

        var first = await Post(body, key);
        var second = await Post(body, key);                        // same key → replay, no second row
        var third = await Post(body, Guid.NewGuid().ToString());   // fresh key → a new event

        second.Id.Should().Be(first.Id, "a retry with the same Idempotency-Key must not schedule twice");
        third.Id.Should().NotBe(first.Id);

        async Task<CreatedResponse> Post(ScheduleBody b, string idem)
        {
            var response = await fixture.Client.SendAsync(Request(HttpMethod.Post, "/api/events", TenantA, b, idem));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!;
        }
    }
}
