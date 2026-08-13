namespace BuildingBlocks.Application.Diagnostics;

/// <summary>
/// Tracks whether every module's EF migrations have been applied at startup. Each module registers its
/// migrator (incrementing <see cref="Register"/> at DI-config time, before the host even starts), and the
/// migrator calls <see cref="MarkApplied"/> once its migrations run. A startup health check reads
/// <see cref="AllApplied"/> so external orchestration (e.g. Debezium in docker-compose) can wait for the
/// schema to exist before it builds its publication. Shared kernel so the migrator (Infrastructure) and the
/// health check (ServiceDefaults) agree on one instance.
/// </summary>
public sealed class MigrationReadinessTracker
{
    private int _expected;
    private int _applied;

    /// <summary>Called once per module migrator at registration time.</summary>
    public void Register() => Interlocked.Increment(ref _expected);

    /// <summary>Called once per module migrator after its migrations have been applied.</summary>
    public void MarkApplied() => Interlocked.Increment(ref _applied);

    public int Expected => Volatile.Read(ref _expected);
    public int Applied => Volatile.Read(ref _applied);

    /// <summary>True once every registered migrator has applied (vacuously true when none are registered).</summary>
    public bool AllApplied => Applied >= Expected;
}
