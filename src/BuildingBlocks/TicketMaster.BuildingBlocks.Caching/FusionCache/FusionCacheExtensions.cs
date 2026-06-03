using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace TicketMaster.BuildingBlocks.Caching.FusionCache;

/// <summary>
/// FusionCache as an L1 (in-memory, read-local) + L2 (Redis, write-through) cache. Reads hit L1 first and only
/// fall through to L2 on a miss; writes go to L2 and a Redis backplane broadcasts an L1 eviction to the other
/// nodes. L2 payloads use MessagePack (smaller + faster than JSON). All keys are app-name prefixed. Requires
/// <see cref="Redis.RedisInfrastructureExtensions.AddRedisInfrastructure"/> (shared multiplexer + L2) first.
/// </summary>
public static class FusionCacheExtensions
{
    public static WebApplicationBuilder AddAppCache(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        // Backplane on the same shared multiplexer (sentinel-aware), resolved from DI.
        builder.Services.AddSingleton<IFusionCacheBackplane>(sp =>
            new RedisBackplane(Options.Create(new RedisBackplaneOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>())
            })));

        builder.Services.AddFusionCache()
            .WithOptions(o => o.CacheKeyPrefix = $"{serviceName}:cache:")
            .WithDefaultEntryOptions(o =>
            {
                o.Duration = TimeSpan.FromMinutes(5);            // L1 + L2 logical TTL
                o.JitterMaxDuration = TimeSpan.FromSeconds(10);  // anti herd / cache stampede
                o.IsFailSafeEnabled = true;                      // serve stale if L2 errors
                o.FailSafeMaxDuration = TimeSpan.FromHours(1);
                o.FactorySoftTimeout = TimeSpan.FromMilliseconds(100);
                o.DistributedCacheDuration = TimeSpan.FromMinutes(10);
            })
            .WithSerializer(new FusionCacheNeueccMessagePackSerializer())
            .WithRegisteredDistributedCache()
            .WithRegisteredBackplane();

        return builder;
    }
}
