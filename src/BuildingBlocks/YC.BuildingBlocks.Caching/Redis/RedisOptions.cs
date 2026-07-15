namespace YC.BuildingBlocks.Caching.Redis;

/// <summary>
/// Redis connection configuration. Bound from the <c>Redis</c> config section. Supports a single standalone
/// node (dev/test) and a Sentinel-managed master/replica set (HA prod) behind the same code path.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Standalone (single node) or Sentinel (HA master/replica set).</summary>
    public RedisMode Mode { get; set; } = RedisMode.Standalone;

    /// <summary>Standalone connection string, e.g. <c>localhost:6379,abortConnect=false</c>.</summary>
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>Sentinel endpoints (<c>host:port</c>), used when <see cref="Mode"/> is Sentinel.</summary>
    public string[] Sentinels { get; set; } = [];

    /// <summary>Sentinel master set name (the monitored master). Defaults to the Redis Sentinel convention; override per app.</summary>
    public string ServiceName { get; set; } = "mymaster";

    /// <summary>Optional auth password for the data nodes (master/replicas).</summary>
    public string Password { get; set; }

    public int Database { get; set; }

    public int ConnectTimeoutMs { get; set; } = 2000;
}

public enum RedisMode
{
    Standalone,
    Sentinel
}
