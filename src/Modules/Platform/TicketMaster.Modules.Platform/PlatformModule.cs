using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Infrastructure.Inbox;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;
using TicketMaster.ServiceDefaults.Modularity;

namespace TicketMaster.Modules.Platform;

public sealed class PlatformModule : IModule
{
    public string Name => "Platform";

    public Assembly Assembly => typeof(PlatformModule).Assembly;

    public void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDomainEventDispatch();
        services.AddDbContext<PlatformDbContext>((sp, o) =>
            o.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
                // snake_case tables/columns derived from CLR names — no hand-written HasColumnName.
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IOutboxWriter>(sp => new OutboxWriter(sp.GetRequiredService<PlatformDbContext>()));
        services.AddScoped<IInboxStore>(sp => new InboxStore(sp.GetRequiredService<PlatformDbContext>()));

        // Auto-apply this module's migrations at startup.
        services.AddDbContextMigration<PlatformDbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPlatformEndpoints();
}
