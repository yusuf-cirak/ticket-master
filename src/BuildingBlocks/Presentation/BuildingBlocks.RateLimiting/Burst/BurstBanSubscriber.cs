#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Subscribes to the burst-ban pub/sub channel and mirrors each broadcast ban into this node's local cache, so
/// <see cref="HybridBurstBanStore"/> can answer the per-request ban check without touching Redis. Registered only in
/// <see cref="RateLimitingMode.Distributed"/>, alongside the hybrid store.
/// </summary>
internal sealed class BurstBanSubscriber(
    IConnectionMultiplexer mux,
    IBurstBanStore store,
    IOptions<RateLimitingOptions> options) : IHostedService
{
    private ChannelMessageQueue? _queue;
    private string Prefix => options.Value.KeyPrefix ?? "app";
    private RedisChannel Channel => RedisChannel.Literal($"{Prefix}{RateLimitingConstants.BanChannelSuffix}");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (store is not HybridBurstBanStore hybrid)
            return; // only meaningful with the hybrid store

        _queue = await mux.GetSubscriber().SubscribeAsync(Channel);
        _queue.OnMessage(msg =>
        {
            if (HybridBurstBanStore.TryDecode(msg.Message, out var key, out var until))
                hybrid.Mirror(key, until);
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
            await _queue.UnsubscribeAsync();
    }
}
