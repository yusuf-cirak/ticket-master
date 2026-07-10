using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using TicketMaster.BuildingBlocks.Domain.Observability;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Stamps every log record with the propagated cross-cutting baggage (<c>correlation_id</c>, <c>causation_id</c>,
/// <c>user_id</c>, <c>client_ip</c>) so durable, business-level context appears on every log line — including
/// consumer logs, where the baggage is restored from the message headers. Baggage is the single source of truth
/// (set once at the HTTP boundary / consume filter); this surfaces it on logs without threading it through call
/// sites.
/// <para>
/// Hot path, allocation-minimal: it reads a fixed, known key set with <see cref="Activity.GetBaggageItem"/>
/// (no baggage enumerator / iterator allocation — <c>Activity.Baggage</c> would allocate one per call); a record
/// whose baggage is empty or already present allocates nothing; when there is something to add it builds exactly
/// one pre-sized list (no growth re-allocation). Values are snapshotted because the record may be exported later
/// on a batch thread, off the originating Activity. New propagated keys must be added to <see cref="BaggageKeys"/>.
/// </para>
/// </summary>
internal sealed class BaggageLogRecordProcessor : BaseProcessor<LogRecord>
{
    // The complete set of baggage keys this app propagates (see CorrelationMiddleware, RequestEnrichmentMiddleware,
    // and the Kafka consume filter). Reading these directly avoids enumerating the baggage linked list.
    private static readonly string[] BaggageKeys =
    [
        TelemetryConstants.CorrelationIdKey,
        TelemetryConstants.CausationIdKey,
        TelemetryConstants.UserIdKey,
        TelemetryConstants.ClientIpKey
    ];

    public override void OnEnd(LogRecord record)
    {
        if (Activity.Current is not { } activity)
            return;

        var attributes = record.Attributes;
        List<KeyValuePair<string, object>> merged = null;

        foreach (var key in BaggageKeys) // foreach over an array — index-based, no enumerator allocation
        {
            if (activity.GetBaggageItem(key) is not { } value || ContainsKey(attributes, key))
                continue;

            merged ??= Seed(attributes, BaggageKeys.Length);
            merged.Add(new KeyValuePair<string, object>(key, value));
        }

        if (merged is not null)
            record.Attributes = merged;
    }

    private static List<KeyValuePair<string, object>> Seed(
        IReadOnlyList<KeyValuePair<string, object>> existing, int headroom)
    {
        var count = existing?.Count ?? 0;
        var list = new List<KeyValuePair<string, object>>(count + headroom);
        for (var i = 0; i < count; i++)
            list.Add(existing![i]);

        return list;
    }

    private static bool ContainsKey(IReadOnlyList<KeyValuePair<string, object>> attributes, string key)
    {
        if (attributes is null)
            return false;

        for (var i = 0; i < attributes.Count; i++)
            if (string.Equals(attributes[i].Key, key, StringComparison.Ordinal))
                return true;

        return false;
    }
}
