using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Verifies the distributed rate limiter end-to-end against a real Redis: the anonymous burst limit
/// (configured 20/5s × the anon tier 0.25 = 5 permits) is enforced, and the 6th request in the window is
/// rejected with an RFC-7807 429 + Retry-After.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RateLimitingTest(SpineFixture fixture) 
{
    private const int AnonymousBurstPermits = 5; // ceil(20 * 0.25)

    [Fact]
    public async Task BurstLimit_IsEnforced_Returns429WithProblemDetailsAndRetryAfter()
    {
        // The first <permit> requests in the window are admitted (not 429).
        await fixture.ResetRedisAsync(); // start from a clean rate-limit window (Redis is shared across tests)

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

    /// <summary>
    /// Exceeding the burst window arms a fixed 10s soft-ban that outlives the 5s sliding window: the caller stays 429
    /// with a Retry-After ≥ 9s on the next request, and remains 429 even after the sliding window has fully reset.
    /// </summary>
    [Fact]
    public async Task BurstBan_OutlivesSlidingWindow_KeepsCaller429()
    {
        await fixture.ResetRedisAsync(); // clean window AND ban key

        // Exhaust the burst permits.
        for (var i = 0; i < AnonymousBurstPermits; i++)
            await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = $"ban-{i}" });

        // Breach → arms the 10s ban.
        var breach = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = "ban-breach" });
        breach.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // The next request is short-circuited by the ban, not the window: a Retry-After ≥ 9s can only come from the
        // 10s ban, since the sliding window is only 5s.
        var banned = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = "ban-next" });
        banned.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        banned.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromSeconds(9), "the 10s burst ban outlives the 5s sliding window");

        // After the sliding window has fully reset (6s > 5s window) the caller is STILL 429 — proof the ban persists
        // independently of the window (a window-only limiter would have admitted this request).
        await Task.Delay(TimeSpan.FromSeconds(6));
        var stillBanned = await fixture.Client.PostAsJsonAsync("/api/platform/ping", new { note = "ban-after-window" });
        stillBanned.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
