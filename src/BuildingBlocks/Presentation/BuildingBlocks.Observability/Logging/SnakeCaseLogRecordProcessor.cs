using OpenTelemetry;
using OpenTelemetry.Logs;

namespace BuildingBlocks.Observability;

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

        // Allocate the rewrite list only once a key actually differs — a semconv-only record (all keys already
        // snake/dotted, the common case) does zero allocation and skips the reassignment entirely.
        List<KeyValuePair<string, object>> rewritten = null;
        for (var i = 0; i < attributes.Count; i++)
        {
            var attribute = attributes[i];

            // Leave OTel's special placeholder keys (e.g. "{OriginalFormat}", which carries the message
            // template) untouched — tools key on them, and snake_casing inside braces is meaningless.
            var key = attribute.Key.Length > 0 && attribute.Key[0] == '{'
                ? attribute.Key
                : SnakeCase.Convert(attribute.Key);

            if (key == attribute.Key)
            {
                rewritten?.Add(attribute);
                continue;
            }

            // First change: materialize the list and back-fill the unchanged prefix.
            if (rewritten is null)
            {
                rewritten = new List<KeyValuePair<string, object>>(attributes.Count);
                for (var j = 0; j < i; j++)
                    rewritten.Add(attributes[j]);
            }

            rewritten.Add(new KeyValuePair<string, object>(key, attribute.Value));
        }

        if (rewritten is not null)
            record.Attributes = rewritten;
    }
}
