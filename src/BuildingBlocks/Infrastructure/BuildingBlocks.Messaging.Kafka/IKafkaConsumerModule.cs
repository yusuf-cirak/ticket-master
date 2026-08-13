using MassTransit;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Implemented by a feature module that consumes Kafka, so the host wires the rider generically instead of naming each
/// module's consumers and topics. The module contributes to the two MassTransit rider scopes: <see cref="AddConsumers"/>
/// (rider registration) and <see cref="MapTopics"/> (the Kafka factory). The host owns only the bus + host address.
/// </summary>
public interface IKafkaConsumerModule
{
    /// <summary>Register the module's rider consumers (e.g. <c>rider.AddConsumer&lt;MyConsumer&gt;()</c>).</summary>
    void AddConsumers(IRiderRegistrationConfigurator rider);

    /// <summary>
    /// Map the module's Kafka topic endpoints (e.g. <c>kafka.TopicEndpoint&lt;MyEvent&gt;(topic, group, e =&gt;
    /// e.ApplyDefaults(context))</c>). Topic names come from config the module read during its own <c>Add</c>.
    /// </summary>
    void MapTopics(IRiderRegistrationContext context, IKafkaFactoryConfigurator kafka);
}
