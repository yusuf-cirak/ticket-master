using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace TicketMaster.BuildingBlocks.Caching.Redis;

/// <summary>
/// Wires the shared Redis connection: one sentinel-aware <see cref="IConnectionMultiplexer"/> singleton, the
/// StackExchange.Redis distributed cache (FusionCache's L2), and a Redis readiness health check — all on that
/// same connection, so there is exactly one socket set to the master.
/// </summary>
public static class RedisInfrastructureExtensions
{
    public static WebApplicationBuilder AddRedisInfrastructure(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        var multiplexer = RedisConnectionFactory.Create(options);
        builder.Services.AddSingleton(multiplexer);
        builder.Services.AddSingleton<TicketMaster.BuildingBlocks.Application.Abstractions.Locking.IDistributedLock, RedisDistributedLock>();
        builder.Services.AddSingleton<TicketMaster.BuildingBlocks.Domain.Security.ITokenBlacklist, RedisTokenBlacklist>();

        // L2 for FusionCache + any IDistributedCache consumer. Keys are app-prefixed.
        builder.Services.AddStackExchangeRedisCache(redis =>
        {
            redis.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
            redis.InstanceName = $"{serviceName}:";
        });

        builder.Services.AddHealthChecks()
            .AddRedis(multiplexer, name: "redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

        return builder;
    }
}
