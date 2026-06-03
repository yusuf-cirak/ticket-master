using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Verifies the distributed rate limiter end-to-end against a real Redis: the anonymous burst limit
/// (configured 20/5s × the anon tier 0.25 = 5 permits) is enforced, and the 6th request in the window is
/// rejected with an RFC-7807 429 + Retry-After.
/// </summary>
public sealed class RateLimitingTest(SpineFixture fixture) : IClassFixture<SpineFixture>
{
    private const int AnonymousBurstPermits = 5; // ceil(20 * 0.25)

    [Fact]
    public async Task BurstLimit_IsEnforced_Returns429WithProblemDetailsAndRetryAfter()
    {
        // The first <permit> requests in the window are admitted (not 429).
        for (var i = 0; i < AnonymousBurstPermits; i++)
        {
            var ok = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = $"rl-{i}" });
            ok.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"request #{i + 1} is within the burst limit of {AnonymousBurstPermits}");
        }

        // The next request exceeds the limit → 429 with a problem body + Retry-After.
        var rejected = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = "rl-over" });

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.Contains("Retry-After").Should().BeTrue("a 429 must tell the caller when to retry");
        var body = await rejected.Content.ReadAsStringAsync();
        body.Should().Contain("Too Many Requests");
    }
}
