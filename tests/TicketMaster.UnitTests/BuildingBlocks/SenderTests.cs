using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record Echo(string Text) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<Echo, string>
{
    public Task<string> Handle(Echo request, CancellationToken ct) => Task.FromResult(request.Text);
}

public sealed class UpperBehavior : IPipelineBehavior<Echo, string>
{
    public async Task<string> Handle(Echo request, RequestHandlerDelegate<string> next, CancellationToken ct)
        => (await next()).ToUpperInvariant();
}

public class SenderTests
{
    private static ISender Build(bool withBehavior)
    {
        var services = new ServiceCollection();
        services.AddApplicationMessaging(typeof(SenderTests).Assembly);
        if (withBehavior)
            services.AddTransient<IPipelineBehavior<Echo, string>, UpperBehavior>();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_routes_to_handler()
    {
        var result = await Build(withBehavior: false).Send(new Echo("hi"));
        result.Should().Be("hi");
    }

    [Fact]
    public async Task Send_runs_pipeline_behavior()
    {
        var result = await Build(withBehavior: true).Send(new Echo("hi"));
        result.Should().Be("HI");
    }
}
