using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Infrastructure.Inbox;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;

namespace TicketMaster.Modules.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PlatformDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
                // snake_case tables/columns derived from CLR names — no hand-written HasColumnName.
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IOutboxWriter>(sp => new OutboxWriter(sp.GetRequiredService<PlatformDbContext>()));
        services.AddScoped<IInboxStore>(sp => new InboxStore(sp.GetRequiredService<PlatformDbContext>()));

        // Auto-apply this module's migrations at startup.
        services.AddDbContextMigration<PlatformDbContext>();

        return services;
    }
}
