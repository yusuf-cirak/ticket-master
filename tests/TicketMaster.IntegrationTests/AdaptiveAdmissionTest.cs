using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Layer-2 adaptive admission control end-to-end. The AIMD-driven admission limit is forced (not inferred from real
/// load) by overriding <c>RateLimiting:Adaptive</c> on a derived host: <c>MaxLimit = MinLimit = 1</c> pins the gate to
/// a single concurrent slot, so saturation is deterministic. The Layer-1 burst limit is lifted on that host so this
/// test isolates the Layer-2 gate (503) from the Layer-1 limiter (429).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdaptiveAdmissionTest(SpineFixture fixture)
{
    [Fact]
    public async Task Gate_sheds_503_when_saturated_then_admits_after_it_clears()
    {
        await fixture.ResetRedisAsync();

        // Reuse the fixture's containers (Postgres/Kafka/Redis) but layer a one-slot admission gate on top. The
        // burst PermitLimit is raised so concurrency reaches the gate instead of tripping the Layer-1 limiter first.
        await using var factory = fixture.Factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("RateLimiting:Adaptive:MaxLimit", "1");
            host.UseSetting("RateLimiting:Adaptive:MinLimit", "1");
            host.UseSetting("RateLimiting:Policies:burst:PermitLimit", "100000");
        });
        var client = factory.CreateClient();

        // A single admission slot + many concurrent slow-ish (DB-backed) requests ⇒ the system is saturated and
        // at least one request is shed with 503 while the one in-flight request holds the slot.
        var responses = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(i => client.PostAsJsonAsync("/api/platform/ping", new { note = $"adaptive-{i}" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should().BeGreaterThan(0, "with a single admission slot, concurrent load must shed at least one 503");

        var shed = responses.First(r => r.StatusCode == HttpStatusCode.ServiceUnavailable);
        shed.Headers.Contains("Retry-After").Should().BeTrue("a 503 must tell the caller when to retry");
        (await shed.Content.ReadAsStringAsync()).Should().Contain("Service Unavailable");

        // Saturation has cleared (all concurrent requests completed) ⇒ the single slot is free again and a lone
        // request is admitted, not shed.
        var after = await client.PostAsJsonAsync("/api/platform/ping", new { note = "adaptive-after" });
        after.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Fail-open: under the default config the gate admits freely (MaxLimit = 1000) and never depends on Redis for
    /// enforcement (it's a pure in-process counter), so a request flows through the adaptive layer unimpeded.
    /// </summary>
    [Fact]
    public async Task Gate_admits_under_normal_config()
    {
        await fixture.ResetRedisAsync();

        var resp = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = "adaptive-open" });

        resp.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable,
            "under normal admission limits the Layer-2 gate must not shed traffic");
    }
}
