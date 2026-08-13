#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using BuildingBlocks.RateLimiting;
using Xunit;

namespace TicketMaster.UnitTests.RateLimiting;

// The Redis publish/store path is integration-tested; here we cover the local mirror + wire codec, which run on
// every request and every pub/sub message. The mux is unused by Mirror/GetRemaining, so a null seam is safe.
public class HybridBurstBanStoreTests
{
    private static HybridBurstBanStore Build() => new(
        new MemoryCache(new MemoryCacheOptions()),
        mux: null!,
        Options.Create(new RateLimitingOptions { KeyPrefix = "t" }),
        TimeProvider.System);

    [Fact]
    public async Task Mirrored_ban_is_readable_locally_with_positive_remaining()
    {
        var store = Build();
        store.Mirror("user:42|/api/x", DateTimeOffset.UtcNow.AddSeconds(30));

        var remaining = await store.GetRemainingBanAsync("user:42|/api/x", default);

        remaining.Should().NotBeNull();
        remaining!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_unbanned_key_reads_as_null_without_touching_redis()
    {
        (await Build().GetRemainingBanAsync("nobody", default)).Should().BeNull();
    }

    [Fact]
    public async Task An_already_elapsed_mirror_reads_as_unbanned()
    {
        var store = Build();
        store.Mirror("k", DateTimeOffset.UtcNow.AddSeconds(-1)); // already past → not stored

        (await store.GetRemainingBanAsync("k", default)).Should().BeNull();
    }

    [Fact]
    public void Wire_codec_roundtrips_even_when_the_key_contains_a_pipe()
    {
        var until = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var payload = HybridBurstBanStore.Encode("user:42|/api/items/{id}", until);

        HybridBurstBanStore.TryDecode(payload, out var key, out var decoded).Should().BeTrue();
        key.Should().Be("user:42|/api/items/{id}");
        decoded.Should().Be(until);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-newline")]
    [InlineData("notanumber\nkey")]
    [InlineData("123\n")]
    public void Wire_codec_rejects_malformed_payloads(string raw)
    {
        HybridBurstBanStore.TryDecode(raw, out _, out _).Should().BeFalse();
    }
}
