using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application.Abstractions.Locking;
using BuildingBlocks.Application.Abstractions.Security;

namespace BuildingBlocks.Locking;

/// <summary>
/// Redis-backed <see cref="IDistributedLock"/> + <see cref="ITokenBlacklist"/>. Opt-in and independent of the cache:
/// both resolve the shared <see cref="StackExchange.Redis.IConnectionMultiplexer"/> from DI, so call this <b>after</b>
/// the multiplexer is registered (e.g. <c>AddRedisInfrastructure</c>). Apps that need neither simply don't reference
/// this package.
/// </summary>
public static class RedisLockingExtensions
{
    public static WebApplicationBuilder AddRedisLocking(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        builder.Services.AddSingleton<ITokenBlacklist, RedisTokenBlacklist>();
        return builder;
    }
}
