namespace TicketMaster.BuildingBlocks.Caching.Redis;

/// <summary>
/// Redis connection configuration. Bound from the <c>Redis</c> config section. Supports a single standalone
/// node (dev/test) and a Sentinel-managed master/replica set (HA prod) behind the same code path.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Standalone (single node) or Sentinel (HA master/replica set).</summary>
    public RedisMode Mode { get; init; } = RedisMode.Standalone;

    /// <summary>Standalone connection string, e.g. <c>localhost:6379,abortConnect=false</c>.</summary>
    public string ConnectionString { get; init; } = "localhost:6379,abortConnect=false";

    /// <summary>Sentinel endpoints (<c>host:port</c>), used when <see cref="Mode"/> is Sentinel.</summary>
    public string[] Sentinels { get; init; } = [];

    /// <summary>Sentinel master set name (the monitored master), e.g. <c>tm-master</c>.</summary>
    public string ServiceName { get; init; } = "tm-master";

    /// <summary>Optional auth password for the data nodes (master/replicas).</summary>
    public string? Password { get; init; }

    public int Database { get; init; }

    public int ConnectTimeoutMs { get; init; } = 2000;
}

public enum RedisMode
{
    Standalone,
    Sentinel
}
