using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Persistence.Inbox;
using BuildingBlocks.Persistence.Outbox;
using BuildingBlocks.Persistence.EfCore;
using BuildingBlocks.Persistence.EfCore.Interceptors;
using BuildingBlocks.Modularity;

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
                .UseModuleConventions() // snake_case + no-tracking default
                .AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>()));

        // Keyed by this module's DbContext: an unkeyed IOutboxWriter would collide across modules (last registration
        // wins), so a second module's writer would silently hijack this one and write to the wrong outbox table.
        services.AddKeyedScoped<IOutboxWriter>(nameof(PlatformDbContext),
            (sp, _) => new OutboxWriter(sp.GetRequiredService<PlatformDbContext>()));
        services.AddScoped<IInboxStore>(sp => new InboxStore(sp.GetRequiredService<PlatformDbContext>()));

        // Auto-apply this module's migrations at startup.
        services.AddDbContextMigration<PlatformDbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPlatformEndpoints();
}
