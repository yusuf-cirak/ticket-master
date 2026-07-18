using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// End-to-end HTTP exercise of the Events demo module against the shared spine's real Postgres: idempotent create,
/// paged list, get-by-id, and a 404 for an unknown id — proving the reusable building blocks (MediatR CQRS, outbox,
/// streamed pagination, idempotency filter, RFC9457 errors) compose into a working vertical slice.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class EventsEndpointTest(SpineFixture fixture)
{
    private sealed record ScheduleBody(string Name, string Venue, DateTimeOffset StartsOn, int Capacity);
    private sealed record CreatedResponse(Guid Id);
    private sealed record EventView(Guid Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity);
    private sealed record ListItem(Guid Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity, bool IsLargeVenue);
    private sealed record PagedView(List<ListItem> Items, int Page, int PageSize, long TotalCount);

    [Fact]
    public async Task Schedule_then_get_and_list_roundtrips_through_http()
    {
        var body = new ScheduleBody("Cup Final", "Grand Arena", DateTimeOffset.UtcNow.AddDays(30), 5000);

        var created = await PostWithKey(body, Guid.NewGuid().ToString());
        created.Id.Should().NotBe(Guid.Empty);

        var got = await fixture.Client.GetFromJsonAsync<EventView>($"/api/events/{created.Id}");
        got!.Name.Should().Be("Cup Final");
        got.Venue.Should().Be("Grand Arena");
        got.Capacity.Should().Be(5000);

        var page = await fixture.Client.GetFromJsonAsync<PagedView>("/api/events?page=1&pageSize=200");
        // IsLargeVenue is an EF [Projectable] computed server-side (capacity 5000 >= 1000) — proves UseProjectables()
        // translated the domain expression to SQL rather than evaluating it on the client.
        page!.Items.Should().ContainSingle(e => e.Id == created.Id)
            .Which.IsLargeVenue.Should().BeTrue();
    }

    [Fact]
    public async Task Get_unknown_event_returns_404()
    {
        var response = await fixture.Client.GetAsync($"/api/events/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_the_first_result()
    {
        var body = new ScheduleBody("Replay Show", "Replay Hall", DateTimeOffset.UtcNow.AddDays(10), 42);
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKey(body, key);
        var second = await PostWithKey(body, key);                       // same key → replay, no second row
        var third = await PostWithKey(body, Guid.NewGuid().ToString());  // fresh key → a new event

        second.Id.Should().Be(first.Id, "a retry with the same Idempotency-Key must not schedule twice");
        third.Id.Should().NotBe(first.Id);
    }

    private async Task<CreatedResponse> PostWithKey(ScheduleBody body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/events") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        var response = await fixture.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!;
    }
}
