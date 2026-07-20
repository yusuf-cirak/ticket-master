using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using BuildingBlocks.Observability;

namespace TicketMaster.UnitTests.Observability;

public class ObservabilityBackendTests
{
    [Theory]
    [InlineData("OpenTelemetry")]
    [InlineData("Serilog")]
    public void AddObservability_wires_the_selected_logging_backend_and_logs_without_throwing(string backend)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Observability:LoggingBackend"] = backend;

        builder.AddObservability("test-service");

        using var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<ObservabilityBackendTests>>();

        var act = () => logger.LogInformation("boot check {Value}", 42);

        act.Should().NotThrow($"the {backend} logging backend must wire cleanly");
    }
}
