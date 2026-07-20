#nullable enable
using System;
using System.Diagnostics;
using FluentAssertions;
using BuildingBlocks.Observability;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace TicketMaster.UnitTests.Observability;

public class SerilogEnricherTests
{
    private static readonly MessageTemplate Template = new MessageTemplateParser().Parse("test");
    private static readonly ILogEventPropertyFactory Factory = new PropertyFactory();

    private static LogEvent EventWith(params LogEventProperty[] properties) =>
        new(DateTimeOffset.UtcNow, LogEventLevel.Information, exception: null, Template, properties);

    [Fact]
    public void SnakeCaseMasking_renames_keys_and_redacts_sensitive_values()
    {
        var logEvent = EventWith(
            new LogEventProperty("OrderId", new ScalarValue(5)),
            new LogEventProperty("Password", new ScalarValue("hunter2")));

        new SnakeCaseMaskingEnricher().Enrich(logEvent, Factory);

        logEvent.Properties.Should().ContainKey("order_id").And.NotContainKey("OrderId");
        ((ScalarValue)logEvent.Properties["order_id"]).Value.Should().Be(5);

        logEvent.Properties.Should().ContainKey("password").And.NotContainKey("Password");
        ((ScalarValue)logEvent.Properties["password"]).Value.Should().Be("***", "sensitive values are masked, never logged");
    }

    [Fact]
    public void Baggage_copies_the_correlation_ids_from_the_ambient_activity()
    {
        using var activity = new Activity("test").Start();
        activity.AddBaggage("correlation_id", "corr-1");
        activity.AddBaggage("causation_id", "cause-1");

        var logEvent = EventWith();
        new BaggageEnricher().Enrich(logEvent, Factory);

        ((ScalarValue)logEvent.Properties["correlation_id"]).Value.Should().Be("corr-1");
        ((ScalarValue)logEvent.Properties["causation_id"]).Value.Should().Be("cause-1");
    }

    private sealed class PropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, value as LogEventPropertyValue ?? new ScalarValue(value));
    }
}
