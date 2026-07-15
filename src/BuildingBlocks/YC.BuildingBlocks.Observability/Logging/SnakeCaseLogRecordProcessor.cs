using OpenTelemetry;
using OpenTelemetry.Logs;

namespace YC.BuildingBlocks.Observability;

/// <summary>
/// Rewrites every log attribute key to snake_case (OpenTelemetry semantic-convention casing) so the
/// app keeps PascalCase message-template properties (<c>{OrderId}</c>) while SigNoz/Grafana receive
/// <c>order_id</c>. Runs in-process on the logging pipeline — the idiomatic OTel extension point —
/// with cached key conversions so the per-record cost is a handful of dictionary lookups.
/// </summary>
internal sealed class SnakeCaseLogRecordProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        if (record.Attributes is not { Count: > 0 } attributes)
            return;

        var changed = false;
        var rewritten = new List<KeyValuePair<string, object>>(attributes.Count);
        foreach (var attribute in attributes)
        {
            // Leave OTel's special placeholder keys (e.g. "{OriginalFormat}", which carries the message
            // template) untouched — tools key on them, and snake_casing inside braces is meaningless.
            if (attribute.Key.Length > 0 && attribute.Key[0] == '{')
            {
                rewritten.Add(attribute);
                continue;
            }

            var key = SnakeCase.Convert(attribute.Key);
            if (key != attribute.Key) changed = true;
            rewritten.Add(new KeyValuePair<string, object>(key, attribute.Value));
        }

        // Only reassign when something actually changed — semconv-only records (all keys already
        // snake/dotted) skip the write entirely.
        if (changed)
            record.Attributes = rewritten;
    }
}
