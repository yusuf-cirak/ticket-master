using YC.BuildingBlocks.Observability;
using System.Collections.Generic;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class BaggageLogRecordProcessorTests
{
    [Fact]
    public void Adds_activity_baggage_entries_as_log_attributes()
    {
        var exported = new List<LogRecord>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("op")!;
        activity.SetBaggage("correlation_id", "corr-99");

        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.AddProcessor(new BaggageLogRecordProcessor());
            o.AddInMemoryExporter(exported);
        }));

        factory.CreateLogger("test").LogInformation("hello");

        var record = exported.Should().ContainSingle().Subject;
        record.Attributes.Should().NotBeNull();
        record.Attributes!.Should().Contain(kv => kv.Key == "correlation_id" && (string)kv.Value == "corr-99");
    }
}
