using BuildingBlocks.Observability;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class UserContextMiddlewareTests
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
    public async Task Sets_user_id_and_client_ip_for_authenticated_request()
    {
        using var listener = StartListening();
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("req")!;

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-123")], "jwt"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        await new UserContextMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        activity.GetBaggageItem("user_id").Should().Be("user-123");
        activity.GetTagItem("user_id").Should().Be("user-123");
        activity.GetBaggageItem("client_ip").Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task Omits_user_id_for_anonymous_request_but_still_sets_client_ip()
    {
        using var listener = StartListening();
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("req")!;

        var context = new DefaultHttpContext(); // unauthenticated principal
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        await new UserContextMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        activity.GetBaggageItem("user_id").Should().BeNull();
        activity.GetBaggageItem("client_ip").Should().Be("203.0.113.7");
    }
}
