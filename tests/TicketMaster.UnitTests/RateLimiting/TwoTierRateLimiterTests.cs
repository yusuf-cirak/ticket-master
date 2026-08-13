#nullable enable
using System.Threading.RateLimiting;
using FluentAssertions;
using BuildingBlocks.RateLimiting;
using Xunit;

namespace TicketMaster.UnitTests.RateLimiting;

public class TwoTierRateLimiterTests
{
    [Fact]
    public async Task Under_cap_delegates_to_the_redis_tier()
    {
        var redis = new CountingRateLimiter();
        using var two = new TwoTierRateLimiter(LocalWithCap(2), redis);

        var lease = await two.AcquireAsync(1);

        lease.IsAcquired.Should().BeTrue();
        redis.Calls.Should().Be(1); // authoritative count consulted
    }

    [Fact]
    public async Task Over_cap_rejects_locally_without_a_redis_round_trip()
    {
        var redis = new CountingRateLimiter();
        using var two = new TwoTierRateLimiter(LocalWithCap(1), redis);

        (await two.AcquireAsync(1)).IsAcquired.Should().BeTrue(); // consumes the single local permit
        var rejected = await two.AcquireAsync(1);

        rejected.IsAcquired.Should().BeFalse();
        redis.Calls.Should().Be(1); // the flood-shield reject never reached Redis
    }

    private static FixedWindowRateLimiter LocalWithCap(int cap) => new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = cap,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    });

    // Stand-in for the Redis tier: always grants, counts how often it is actually consulted.
    private sealed class CountingRateLimiter : RateLimiter
    {
        public int Calls;
        private readonly RateLimiter _inner = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_000_000,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

        public override TimeSpan? IdleDuration => _inner.IdleDuration;
        public override RateLimiterStatistics? GetStatistics() => _inner.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Calls++;
            return _inner.AttemptAcquire(permitCount);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.AcquireAsync(permitCount, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
        }
    }
}
