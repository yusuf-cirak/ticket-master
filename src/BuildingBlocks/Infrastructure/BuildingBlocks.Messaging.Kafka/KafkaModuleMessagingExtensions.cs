using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Kafka;

public static class KafkaModuleMessagingExtensions
{
    /// <summary>
    /// Wires MassTransit + the Kafka rider generically from the given <see cref="IKafkaConsumerModule"/>s: each module
    /// registers its own consumers and topic endpoints, the host supplies only the bootstrap address. Adding a Kafka
    /// consumer to a new module needs no host change — implement <see cref="IKafkaConsumerModule"/> and it is picked up.
    /// </summary>
    public static IServiceCollection AddModuleMessaging(
        this IServiceCollection services, string bootstrap, IReadOnlyList<IKafkaConsumerModule> modules)
    {
        services.AddMassTransit(x =>
        {
            // Inert in-memory bus: MassTransit 8's Kafka rider must attach to a bus, but nothing flows through it
            // (integration events arrive via the outbox → Kafka and are consumed by the rider below).
            x.UsingInMemory();
            x.AddRider(rider =>
            {
                foreach (var module in modules)
                    module.AddConsumers(rider);

                rider.UsingKafka((context, k) =>
                {
                    k.Host(bootstrap);
                    foreach (var module in modules)
                        module.MapTopics(context, k);
                });
            });
        });

        return services;
    }
}
