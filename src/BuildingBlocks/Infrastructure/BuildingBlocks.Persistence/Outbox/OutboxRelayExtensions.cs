#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Persistence.Outbox;

public static class OutboxRelayExtensions
{
    /// <summary>
    /// Runs the polling relay for one module's outbox table (its <paramref name="schema"/>). Register once per module
    /// DbContext, only when <c>Outbox:Relay = Polling</c>. Bind <see cref="OutboxOptions"/> and register an
    /// <see cref="IOutboxEventPublisher"/> (e.g. <c>AddKafkaOutboxPublisher</c>) first.
    /// </summary>
    public static IServiceCollection AddOutboxPolling<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddHostedService(sp => new OutboxPollingService<TContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOutboxEventPublisher>(),
            sp.GetRequiredService<IOptions<OutboxOptions>>(),
            sp.GetRequiredService<ILogger<OutboxPollingService<TContext>>>(),
            schema));
        return services;
    }

    /// <summary>
    /// Registers the polling relay for this module's outbox table only when <c>Outbox:Relay = Polling</c>, so a module
    /// self-registers its own outbox without the host having to know its DbContext. No-op under the Debezium (CDC) relay.
    /// </summary>
    public static IServiceCollection AddOutboxPollingIfEnabled<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        if (configuration.GetValue<OutboxRelay>($"{OutboxOptions.SectionName}:Relay") == OutboxRelay.Polling)
            services.AddOutboxPolling<TContext>(schema);
        return services;
    }
}
