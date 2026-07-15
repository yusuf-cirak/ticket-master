namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Names of the configuration entries the default readiness checks probe. Product-neutral: an app that keys its
/// connection strings differently overrides these instead of adopting the convention. Defaults match the standard
/// keys; a dependency whose entry is absent is simply skipped (no check registered).
/// </summary>
public sealed class HealthCheckSettings
{
    /// <summary>Connection-string name for the Postgres readiness check. Default <c>"Postgres"</c>.</summary>
    public string PostgresConnectionName { get; set; } = "Postgres";

    /// <summary>Connection-string name for the Redis readiness check. Default <c>"Redis"</c>.</summary>
    public string RedisConnectionName { get; set; } = "Redis";

    /// <summary>Configuration key holding the Kafka bootstrap servers. Default <c>"Kafka:BootstrapServers"</c>.</summary>
    public string KafkaBootstrapKey { get; set; } = "Kafka:BootstrapServers";

    /// <summary>Topic the Kafka readiness check produces to. Default <c>"healthchecks"</c>.</summary>
    public string KafkaHealthTopic { get; set; } = "healthchecks";

    /// <summary>Configuration key holding the Debezium Connect URL. Default <c>"Debezium:ConnectUrl"</c>.</summary>
    public string DebeziumConnectUrlKey { get; set; } = "Debezium:ConnectUrl";
}
