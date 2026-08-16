using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record Echo(string Text) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<Echo, string>
{
    public ValueTask<string> Handle(Echo request, CancellationToken ct) => new(request.Text);
}

// A pipeline behavior's participation for a message type is fixed at compile time by the source generator (unlike
// MediatR, where DI registration alone made a behavior runtime-toggleable per container) — so "with a behavior" and
// "without one" need two distinct message types, not one type conditionally wired at runtime.
public sealed record UpperEcho(string Text) : IRequest<string>;

public sealed class UpperEchoHandler : IRequestHandler<UpperEcho, string>
{
    public ValueTask<string> Handle(UpperEcho request, CancellationToken ct) => new(request.Text);
}

public sealed class UpperBehavior : IPipelineBehavior<UpperEcho, string>
{
    public async ValueTask<string> Handle(UpperEcho request, MessageHandlerDelegate<UpperEcho, string> next, CancellationToken ct)
        => (await next(request, ct)).ToUpperInvariant();
}

public class SenderTests
{
    // Self-contained: this compilation has its own Mediator.SourceGenerator install (see the .csproj) specifically
    // so this test can build a working ISender end-to-end without going through the real app's WebApplicationFactory.
    private static ISender Build()
    {
        var services = new ServiceCollection();
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Transient;
            options.PipelineBehaviors = [typeof(UpperBehavior)];
        });
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_routes_to_handler()
    {
        var result = await Build().Send(new Echo("hi"));
        result.Should().Be("hi");
    }

    [Fact]
    public async Task Send_runs_pipeline_behavior()
    {
        var result = await Build().Send(new UpperEcho("hi"));
        result.Should().Be("HI");
    }
}
