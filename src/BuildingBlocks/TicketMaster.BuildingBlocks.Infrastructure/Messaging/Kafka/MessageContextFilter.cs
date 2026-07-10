using System.Diagnostics;
using System.Text;
using MassTransit;
using TicketMaster.BuildingBlocks.Domain.Observability;

namespace TicketMaster.BuildingBlocks.Infrastructure.Messaging.Kafka;

/// <summary>
/// Minimal consume-pipeline filter for the two things MassTransit can't do for our raw-JSON, non-MassTransit
/// (outbox→Debezium→Kafka) producer:
///   • <b>Trace continuity</b> — MassTransit only re-parents from its own <c>MT-Activity-Id</c> header, never
///     from W3C <c>traceparent</c>; this re-parents the consume span to the producer's <c>traceparent</c> so
///     the HTTP request and the consume are one trace. Trace must never be lost.
///   • <b>Correlation + causation on logs</b> — MassTransit maps the <c>CorrelationId</c> header into
///     <see cref="ConsumeContext.CorrelationId"/> natively (but not onto the Activity), and has no causation
///     concept at all; this copies <c>correlation_id</c> (from the context) and <c>causation_id</c> (from the
///     header) into baggage so every consumer log line + any downstream outbox write carries them.
/// MassTransit identity (CorrelationId/MessageId) is mapped by MassTransit itself from the Debezium-emitted
/// headers — this filter no longer maps it.
/// </summary>
public sealed class MessageContextFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public static readonly ActivitySource Source = new("TicketMaster.Messaging");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using var activity = StartActivity(context);

        if (activity is not null)
        {
            // correlation_id: MassTransit already parsed it from the CorrelationId header into the context.
            if (context.CorrelationId is { } correlationId)
                Apply(activity, TelemetryConstants.CorrelationIdKey, correlationId.ToString());

            // causation_id: MassTransit has no equivalent, so read the custom header directly.
            if (Header(context, MessageHeaders.CausationId) is { Length: > 0 } causationId)
                Apply(activity, TelemetryConstants.CausationIdKey, causationId);
        }

        await next.Send(context);
    }

    private static void Apply(Activity activity, string key, string value)
    {
        activity.SetBaggage(key, value);
        activity.SetTag(key, value);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("messageContext");

    private static Activity StartActivity(ConsumeContext context)
    {
        var name = $"{typeof(T).Name} receive";
        return Header(context, MessageHeaders.TraceParent) is { Length: > 0 } traceparent
               && ActivityContext.TryParse(traceparent, Header(context, MessageHeaders.TraceState), out var parent)
            ? Source.StartActivity(name, ActivityKind.Consumer, parent)
            : Source.StartActivity(name, ActivityKind.Consumer);
    }

    private static string Header(ConsumeContext context, string key)
    {
        if (!context.Headers.TryGetHeader(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => raw.ToString()
        };
    }
}
