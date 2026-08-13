using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Messaging.Kafka;
using BuildingBlocks.Persistence.Inbox;
using BuildingBlocks.Persistence.Outbox;
using BuildingBlocks.Persistence.EfCore;
using BuildingBlocks.Persistence.EfCore.Interceptors;
using BuildingBlocks.Modularity;
using TicketMaster.Modules.Platform.Contracts;

namespace TicketMaster.Modules.Platform;

public sealed class PlatformModule : IModule, IKafkaConsumerModule
{
    private string _topic = "tm.platform.events";

    public string Name => "Platform";

    public Assembly Assembly => typeof(PlatformModule).Assembly;

    public void Add(IServiceCollection services, IConfiguration configuration)
    {
        _topic = configuration["Kafka:PlatformTopic"] ?? _topic;

        services.AddDomainEventDispatch();
        services.AddDbContext<PlatformDbContext>((sp, o) =>
            o.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
                // snake_case tables/columns derived from CLR names — no hand-written HasColumnName.
                .UseModuleConventions() // snake_case + no-tracking default
                .AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>()));

        // Keyed by this module's DbContext: an unkeyed IOutboxWriter would collide across modules (last registration
        // wins), so a second module's writer would silently hijack this one and write to the wrong outbox table.
        services.AddKeyedScoped<IOutboxWriter>(nameof(PlatformDbContext),
            (sp, _) => new OutboxWriter(sp.GetRequiredService<PlatformDbContext>()));
        services.AddScoped<IInboxStore>(sp => new InboxStore(sp.GetRequiredService<PlatformDbContext>()));

        // Auto-apply this module's migrations at startup.
        services.AddDbContextMigration<PlatformDbContext>();

        // Self-register the polling relay for this module's outbox (no-op unless Outbox:Relay = Polling).
        services.AddOutboxPollingIfEnabled<PlatformDbContext>(configuration, "platform");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPlatformEndpoints();

    public void AddConsumers(IRiderRegistrationConfigurator rider) => rider.AddConsumer<PingConsumer>();

    public void MapTopics(IRiderRegistrationContext context, IKafkaFactoryConfigurator kafka) =>
        kafka.TopicEndpoint<PingIntegrationEvent>(_topic, "platform", e =>
        {
            e.ApplyDefaults(context); // offset, raw-JSON, context-propagation filter, retry
            e.ConfigureConsumer<PingConsumer>(context);
        });
}
