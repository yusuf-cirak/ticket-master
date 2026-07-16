using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Domain.Diagnostics;

namespace BuildingBlocks.Persistence.EfCore;

/// <summary>
/// Applies a module's EF Core migrations once at startup (before the app serves traffic). Registered
/// per module via <see cref="MigrationExtensions.AddDbContextMigration{TContext}"/>, so each module
/// auto-applies its own migrations and reports completion to the shared <see cref="MigrationReadinessTracker"/>.
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
