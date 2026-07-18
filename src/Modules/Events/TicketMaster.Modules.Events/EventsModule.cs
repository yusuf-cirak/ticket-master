using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Persistence.Outbox;
using BuildingBlocks.Persistence.EfCore;
using BuildingBlocks.Persistence.EfCore.Interceptors;
using BuildingBlocks.Modularity;
using TicketMaster.Modules.Events.Host;
using TicketMaster.Modules.Events.Infrastructure;

namespace TicketMaster.Modules.Events;

/// <summary>
/// The Events bounded context — a full vertical slice that demonstrates the template's building blocks: a Vogen-keyed
/// aggregate, MediatR CQRS, snake_case EF, the transactional outbox, streamed pagination, and idempotent writes.
/// </summary>
public sealed class EventsModule : IModule
{
    public string Name => "Events";

    public Assembly Assembly => typeof(EventsModule).Assembly;

    public void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDomainEventDispatch();
        services.AddDbContext<EventsDbContext>((sp, o) =>
            o.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations", EventsDbContext.Schema))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>()));

        // Keyed by this module's DbContext so it never collides with another module's outbox writer (see PlatformModule).
        services.AddKeyedScoped<IOutboxWriter>(nameof(EventsDbContext),
            (sp, _) => new OutboxWriter(sp.GetRequiredService<EventsDbContext>()));

        // Auto-apply this module's migrations at startup.
        services.AddDbContextMigration<EventsDbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapEventEndpoints();
}
