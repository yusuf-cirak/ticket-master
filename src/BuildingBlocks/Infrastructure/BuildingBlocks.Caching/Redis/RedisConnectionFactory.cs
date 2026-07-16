using StackExchange.Redis;

namespace BuildingBlocks.Caching.Redis;

/// <summary>
/// Builds the one shared <see cref="IConnectionMultiplexer"/> for the app. In Sentinel mode it connects to the
/// sentinels, then resolves the current master via <c>GetSentinelMasterConnection</c> — that connection
/// auto-follows failover when a sentinel promotes a replica. <c>AbortOnConnectFail = false</c> everywhere so a
/// Redis blip never crashes startup (the rate limiter degrades fail-open instead).
/// </summary>
public static class RedisConnectionFactory
{
    public static IConnectionMultiplexer Create(RedisOptions options)
    {
        if (options.Mode == RedisMode.Standalone)
        {
            var standalone = ConfigurationOptions.Parse(options.ConnectionString);
            standalone.AbortOnConnectFail = false;
            standalone.ConnectTimeout = options.ConnectTimeoutMs;
            standalone.DefaultDatabase = options.Database;
            if (options.Password is { Length: > 0 })
                standalone.Password = options.Password;
            return ConnectionMultiplexer.Connect(standalone);
        }

        var sentinelConfig = new ConfigurationOptions
        {
            ServiceName = options.ServiceName,
            AbortOnConnectFail = false,
            ConnectTimeout = options.ConnectTimeoutMs,
            CommandMap = CommandMap.Sentinel,
            TieBreaker = string.Empty
        };
        foreach (var endpoint in options.Sentinels)
            sentinelConfig.EndPoints.Add(endpoint);

        var sentinel = ConnectionMultiplexer.Connect(sentinelConfig);

        var masterConfig = new ConfigurationOptions
        {
            ServiceName = options.ServiceName,
            AbortOnConnectFail = false,
            ConnectTimeout = options.ConnectTimeoutMs,
            DefaultDatabase = options.Database
        };
        if (options.Password is { Length: > 0 })
            masterConfig.Password = options.Password;

        return sentinel.GetSentinelMasterConnection(masterConfig);
    }
}
