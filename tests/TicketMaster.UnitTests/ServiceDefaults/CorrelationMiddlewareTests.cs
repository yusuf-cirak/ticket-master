using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class CorrelationMiddlewareTests
{
    private static ActivityListener StartListening()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Generates_correlation_id_when_request_has_none()
    {
        using var listener = StartListening();
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("req")!;

        var context = new DefaultHttpContext();
        var middleware = new CorrelationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var id = context.Response.Headers["X-Correlation-ID"].ToString();
        id.Should().HaveLength(36).And.MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        activity.GetBaggageItem("correlation_id").Should().Be(id);
        activity.GetTagItem("correlation_id").Should().Be(id);
        context.Response.Headers["X-Trace-Id"].ToString().Should().Be(activity.TraceId.ToString());
    }

    [Fact]
    public async Task Reuses_valid_inbound_guid_correlation_id()
    {
        using var listener = StartListening();
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("req")!;

        var context = new DefaultHttpContext();
        // Uppercase/braced GUID is normalized to canonical lowercase "D" form.
        context.Request.Headers["X-Correlation-ID"] = "{0190A1B2-C3D4-7E5F-8A9B-0C1D2E3F4A5B}";
        var middleware = new CorrelationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        const string expected = "0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b";
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(expected);
        activity.GetBaggageItem("correlation_id").Should().Be(expected);
    }

    [Theory]
    [InlineData("order-12345")]      // not a GUID
    [InlineData("inject\r\nX-Evil: 1")]
    [InlineData("")]
    public async Task Rejects_non_guid_inbound_and_generates_fresh(string inbound)
    {
        using var listener = StartListening();
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("req")!;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = inbound;
        var middleware = new CorrelationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var id = context.Response.Headers["X-Correlation-ID"].ToString();
        id.Should().NotBe(inbound).And.MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
    }
}
