using BuildingBlocks.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using BuildingBlocks.Domain.Diagnostics;
using BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public class MigrationReadinessTests
{
    [Fact]
    public void Fresh_tracker_is_vacuously_applied()
    {
        new MigrationReadinessTracker().AllApplied.Should().BeTrue();
    }

    [Fact]
    public void Registered_but_unapplied_is_not_applied()
    {
        var t = new MigrationReadinessTracker();
        t.Register();
        t.Register();
        t.AllApplied.Should().BeFalse();
        t.Expected.Should().Be(2);
        t.Applied.Should().Be(0);
    }

    [Fact]
    public void Applied_when_every_registered_migrator_reports()
    {
        var t = new MigrationReadinessTracker();
        t.Register();
        t.Register();
        t.MarkApplied();
        t.AllApplied.Should().BeFalse();
        t.MarkApplied();
        t.AllApplied.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_is_healthy_when_no_tracker()
    {
        var result = await new MigrationsAppliedHealthCheck(null).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task HealthCheck_unhealthy_while_pending_then_healthy_when_applied()
    {
        var t = new MigrationReadinessTracker();
        t.Register();
        var check = new MigrationsAppliedHealthCheck(t);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);

        t.MarkApplied();
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }
}
