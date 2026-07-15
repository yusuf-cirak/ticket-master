using Microsoft.Extensions.Diagnostics.HealthChecks;
using YC.BuildingBlocks.Domain.Diagnostics;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Startup health check: Healthy once every module's EF migrations have been applied (so the schema exists).
/// External orchestration gates on this — e.g. Debezium waits for it before building its outbox publication.
/// When no migrators are registered (<paramref name="tracker"/> null) it is vacuously Healthy.
/// </summary>
internal sealed class MigrationsAppliedHealthCheck(MigrationReadinessTracker tracker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (tracker is null || tracker.AllApplied)
            return Task.FromResult(HealthCheckResult.Healthy());

        return Task.FromResult(HealthCheckResult.Unhealthy(
            $"EF migrations pending ({tracker.Applied}/{tracker.Expected})."));
    }
}
