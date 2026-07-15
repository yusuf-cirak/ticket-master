using YC.BuildingBlocks.HealthChecks;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class ShutdownReadinessHealthCheckTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }

    [Fact]
    public async Task Healthy_while_running()
    {
        var check = new ShutdownReadinessHealthCheck(new FakeLifetime());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Unhealthy_once_shutdown_begins()
    {
        var lifetime = new FakeLifetime();
        var check = new ShutdownReadinessHealthCheck(lifetime);

        lifetime.StopApplication(); // SIGTERM → ApplicationStopping fires

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
