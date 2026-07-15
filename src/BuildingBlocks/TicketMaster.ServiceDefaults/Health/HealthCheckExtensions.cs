using Confluent.Kafka;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TicketMaster.BuildingBlocks.Domain.Diagnostics;

namespace TicketMaster.ServiceDefaults;

internal static class HealthCheckExtensions
{
    private const string Ready = "ready";
    private const string Live = "live";
    private const string Startup = "startup";

    /// <summary>
    /// Liveness (thread-pool) + readiness component checks (postgres/redis/kafka/debezium-connect), one
    /// AspNetCore.HealthChecks.* check per dependency, all configured from the standard connection settings.
    /// </summary>
    public static WebApplicationBuilder AddDefaultHealthChecks(this WebApplicationBuilder builder, HealthCheckSettings settings = null)
    {
        settings ??= new HealthCheckSettings();
        var cfg = builder.Configuration;
        
        
        // Liveness
        var checks = builder.Services.AddHealthChecks()
            .AddCheck<ThreadPoolStarvationHealthCheck>("threadpool", tags: [Live]);
        
        
        
        // Startup: schema migrations applied. Tag is its own group (NOT 'ready' — readiness probes the
        // Debezium Connect URL, and Debezium gates on this, which would deadlock). Tracker is optional.
        checks.Add(new HealthCheckRegistration(
            "migrations",
            sp => new MigrationsAppliedHealthCheck(sp.GetService<MigrationReadinessTracker>()),
            failureStatus: null,
            tags: [Startup]));


        // Drain-on-shutdown: readiness fails the instant SIGTERM arrives so the LB stops routing here before the
        // in-flight drain. Singleton + factory registration so a single ApplicationStopping callback is wired.
        builder.Services.AddSingleton<ShutdownReadinessHealthCheck>();
        checks.Add(new HealthCheckRegistration(
            "shutdown",
            sp => sp.GetRequiredService<ShutdownReadinessHealthCheck>(),
            failureStatus: HealthStatus.Unhealthy,
            tags: [Ready]));

        if (cfg.GetConnectionString(settings.PostgresConnectionName) is { Length: > 0 } postgres)
            checks.AddNpgSql(postgres, name: "postgres", tags: [Ready], timeout: TimeSpan.FromSeconds(3));
        if (cfg.GetConnectionString(settings.RedisConnectionName) is { Length: > 0 } redis)
            checks.AddRedis(redis, name: "redis", tags: [Ready], timeout: TimeSpan.FromSeconds(3));
        if (cfg[settings.KafkaBootstrapKey] is { Length: > 0 } bootstrap)
            checks.AddKafka(new ProducerConfig { BootstrapServers = bootstrap }, topic: settings.KafkaHealthTopic, name: "kafka",
                tags: [Ready], timeout: TimeSpan.FromSeconds(5));
        if (cfg[settings.DebeziumConnectUrlKey] is { Length: > 0 } connect)
            checks.AddUrlGroup(new Uri(connect), name: "debezium-connect", tags: [Ready],
                timeout: TimeSpan.FromSeconds(3));

        return builder;
    }

    /// <summary>K8s probes: /health/startup (migrations), /health/liveness (process), /health/readiness (deps), /health.</summary>
    public static WebApplication MapDefaultHealthEndpoints(this WebApplication app)
    {
        var json = new HealthCheckOptions { ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse };

        // Probes are hit every few seconds by k8s — suppress their request logs to keep the signal clean.
        app.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(Startup),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous().SuppressLogging();
        app.MapHealthChecks("/health/liveness", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(Live),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous().SuppressLogging();
        app.MapHealthChecks("/health/readiness", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(Ready),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous().SuppressLogging();
        app.MapHealthChecks("/health", json).AllowAnonymous().SuppressLogging();

        return app;
    }
}