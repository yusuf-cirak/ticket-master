using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Liveness signal: detects thread-pool starvation by measuring whether the pool can schedule a trivial
/// work item within a threshold (HealthChecks:ThreadPoolDelayThresholdMs, default 500ms). If it can't,
/// requests are queueing behind a starved pool and the pod should be restarted.
/// </summary>
internal sealed class ThreadPoolStarvationHealthCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var threshold = TimeSpan.FromMilliseconds(config.GetValue("HealthChecks:ThreadPoolDelayThresholdMs", 500));

        var probe = Task.Run(() => { }, cancellationToken);
        var starved = await Task.WhenAny(probe, Task.Delay(threshold, cancellationToken)).ConfigureAwait(false) != probe;

        ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);
        var data = new Dictionary<string, object>
        {
            ["availableWorkerThreads"] = availableWorkers,
            ["maxWorkerThreads"] = maxWorkers,
            ["pendingWorkItems"] = ThreadPool.PendingWorkItemCount,
            ["thresholdMs"] = threshold.TotalMilliseconds
        };

        return starved
            ? HealthCheckResult.Unhealthy(
                $"Thread pool starved: a trivial work item was not scheduled within {threshold.TotalMilliseconds:F0}ms.", data: data)
            : HealthCheckResult.Healthy("Thread pool is keeping up.", data);
    }
}
