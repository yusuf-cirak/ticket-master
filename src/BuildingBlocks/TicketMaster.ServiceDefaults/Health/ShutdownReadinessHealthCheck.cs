using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Flips readiness to Unhealthy the moment shutdown begins (SIGTERM → <see cref="IHostApplicationLifetime.ApplicationStopping"/>),
/// before the in-flight drain window. The load balancer / k8s sees the failed readiness probe and stops routing new
/// requests to this instance, so traffic drains gracefully instead of hitting a process that's already closing —
/// the difference between a clean rolling deploy and a burst of 502s.
/// Registered as a singleton so exactly one stopping callback is wired for the host's lifetime.
/// </summary>
internal sealed class ShutdownReadinessHealthCheck : IHealthCheck
{
    private volatile bool _stopping;

    public ShutdownReadinessHealthCheck(IHostApplicationLifetime lifetime) =>
        lifetime.ApplicationStopping.Register(() => _stopping = true);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_stopping
            ? HealthCheckResult.Unhealthy("Shutting down — draining.")
            : HealthCheckResult.Healthy());
}
