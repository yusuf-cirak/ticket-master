using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using BuildingBlocks.Caching.FusionCache;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace TicketMaster.UnitTests.Caching;

public class SwrCacheTests
{
    private static readonly TimeSpan Soft = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Hard = TimeSpan.FromHours(1);

    private static IFusionCache NewCache() => new FusionCache(new FusionCacheOptions());

    [Fact]
    public async Task Concurrent_callers_for_one_key_run_the_factory_once()
    {
        using var cache = NewCache();
        var calls = 0;
        var gate = new TaskCompletionSource();

        async Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;          // hold the one execution so the whole herd piles onto it
            return 42;
        }

        // A thundering herd of 25 concurrent readers hits the same cold key.
        var herd = Enumerable.Range(0, 25)
            .Select(_ => cache.GetOrSetSwrAsync("k", Factory, Soft, Hard).AsTask())
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(herd);

        calls.Should().Be(1, "single-flight coalescing must collapse the herd into one factory call");
        results.Should().OnlyContain(v => v == 42);
    }

    [Fact]
    public async Task Fresh_value_is_a_cache_hit_and_does_not_rerun_the_factory()
    {
        using var cache = NewCache();
        var calls = 0;
        Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(7);
        }

        var first = await cache.GetOrSetSwrAsync("k", Factory, Soft, Hard);
        var second = await cache.GetOrSetSwrAsync("k", Factory, Soft, Hard);

        first.Should().Be(7);
        second.Should().Be(7);
        calls.Should().Be(1, "the second read is served from cache");
    }

    [Fact]
    public async Task Hard_ttl_below_soft_ttl_is_rejected()
    {
        using var cache = NewCache();

        var act = async () => await cache.GetOrSetSwrAsync(
            "k", _ => Task.FromResult(1), softTtl: TimeSpan.FromMinutes(5), hardTtl: TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
