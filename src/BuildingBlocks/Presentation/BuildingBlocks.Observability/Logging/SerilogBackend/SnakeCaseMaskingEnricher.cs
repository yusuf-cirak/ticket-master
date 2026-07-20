using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace BuildingBlocks.Observability;

/// <summary>
/// The Serilog counterpart of the <c>SnakeCase</c> + <c>SensitiveData</c> log processors, in one pass and the same
/// order: rename every property key to snake_case (reusing <see cref="SnakeCase.Convert"/>), then redact the value of
/// any key that <see cref="SensitiveDataLogRecordProcessor.IsSensitive"/> flags. Shared helpers keep the two backends'
/// output byte-identical.
/// </summary>
internal sealed class SnakeCaseMaskingEnricher : ILogEventEnricher
{
    private const string Mask = "***";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Snapshot first — we mutate the property collection while iterating.
        foreach (var (key, value) in logEvent.Properties.ToArray())
        {
            var snakeKey = SnakeCase.Convert(key);
            var sensitive = SensitiveDataLogRecordProcessor.IsSensitive(snakeKey);

            if (snakeKey == key && !sensitive)
                continue; // already snake_case and not sensitive — leave it

            logEvent.RemovePropertyIfPresent(key);
            logEvent.AddOrUpdateProperty(new LogEventProperty(snakeKey, sensitive ? new ScalarValue(Mask) : value));
        }
    }
}
