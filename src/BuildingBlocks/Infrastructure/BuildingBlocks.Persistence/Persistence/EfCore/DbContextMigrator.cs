using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Application.Diagnostics;

namespace BuildingBlocks.Persistence.EfCore;

/// <summary>
/// Applies a module's EF Core migrations once at startup (before the app serves traffic). Registered
/// per module via <see cref="MigrationExtensions.AddDbContextMigration{TContext}"/>, so each module
/// auto-applies its own migrations and reports completion to the shared <see cref="MigrationReadinessTracker"/>.
/// <para>
/// Runs at boot on every instance, so a rolling multi-instance deploy briefly runs old and new code against the
/// post-migration schema — safety comes only from how each migration is written, not from anything here. Follow
/// expand/contract for any breaking schema change: (1) an additive, backward-compatible migration (new column
/// nullable or defaulted, new table) that ships and runs first; (2) a deploy of code that writes the new shape
/// while still reading/writing the old one; (3) only once every old-code instance is gone, a later migration that
/// contracts (drops the old column, adds the <c>NOT NULL</c>). Never rename or drop a column and add its
/// replacement in the same migration — that instant is exactly when an old-code instance still expects the old
/// shape.
/// </para>
/// </summary>
public sealed class DbContextMigrator<TContext>(
    IServiceProvider services,
    MigrationReadinessTracker tracker,
    ILogger<DbContextMigrator<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.MigrateAsync(cancellationToken);
        tracker.MarkApplied();
        logger.LogInformation("Applied EF migrations for {DbContext}.", typeof(TContext).Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class MigrationExtensions
{
    /// <summary>Auto-apply <typeparamref name="TContext"/>'s migrations at startup.</summary>
    public static IServiceCollection AddDbContextMigration<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        // Count this migrator at config time (before the host starts) so the startup health check never
        // reports "applied" prematurely.
        GetOrAddTracker(services).Register();
        services.AddHostedService<DbContextMigrator<TContext>>();
        return services;
    }

    private static MigrationReadinessTracker GetOrAddTracker(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(MigrationReadinessTracker))?.ImplementationInstance
            is MigrationReadinessTracker existing)
            return existing;

        var tracker = new MigrationReadinessTracker();
        services.AddSingleton(tracker);
        return tracker;
    }
}
