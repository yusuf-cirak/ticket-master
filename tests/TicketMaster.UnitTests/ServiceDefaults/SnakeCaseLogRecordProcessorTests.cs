using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SnakeCaseLogRecordProcessorTests
{
    [Fact]
    public void Exported_attribute_keys_are_snake_case()
    {
        var exported = new List<LogRecord>();

        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.ParseStateValues = true;
            o.AddProcessor(new SnakeCaseLogRecordProcessor());
            o.AddInMemoryExporter(exported);
        }));

        factory.CreateLogger("test").LogInformation("Order {OrderId} for {UserId}", 7, 42);

        var record = exported.Should().ContainSingle().Subject;
        var keys = new List<string>();
        record.Attributes.Should().NotBeNull();
        foreach (var kv in record.Attributes!) keys.Add(kv.Key);

        keys.Should().Contain("order_id");
        keys.Should().Contain("user_id");
        keys.Should().NotContain("OrderId");
        keys.Should().NotContain("UserId");
        // OTel's message-template placeholder key is left intact (not snake_cased).
        keys.Should().Contain("{OriginalFormat}");
        keys.Should().NotContain("{original_format}");
    }
}
