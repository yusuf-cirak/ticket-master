using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YC.BuildingBlocks.Infrastructure.Persistence.EfCore;
using YC.BuildingBlocks.Infrastructure.Persistence.EfCore.Interceptors;
using TicketMaster.Modules.Iam.Application.Abstractions;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Host;
using TicketMaster.Modules.Iam.Infrastructure;
using YC.BuildingBlocks.Web.Modularity;

namespace TicketMaster.Modules.Iam;

/// <summary>
/// Identity &amp; Access Management module. Owns user/role persistence (ASP.NET Identity over <see cref="IamDbContext"/>
/// keyed by Vogen ids). JWT issuer, Keycloak federation and cached permission authorization are added in later slices.
/// </summary>
public sealed class IamModule : IModule
{
    public string Name => "Iam";

    public Assembly Assembly => typeof(IamModule).Assembly;

    public void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDomainEventDispatch();
        services.AddAuditing();
        services.AddSoftDelete();

        services.AddDbContext<IamDbContext>((sp, o) =>
            o.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npg => npg.MigrationsHistoryTable("__ef_migrations", IamDbContext.Schema))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<DomainEventDispatchInterceptor>(),
                    sp.GetRequiredService<AuditingSaveChangesInterceptor>(),
                    sp.GetRequiredService<SoftDeleteSaveChangesInterceptor>()));

        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<IamDbContext>();

        // Key accounts on Email.Canonical (Gmail dot/+ folded) so CreateAsync/FindByEmailAsync match the aggregate's
        // uniqueness key instead of overriding it with the plain upper-cased address. Registered after AddIdentityCore
        // so it wins over the default UpperInvariantLookupNormalizer.
        services.AddScoped<ILookupNormalizer, CanonicalEmailNormalizer>();

        services.AddOptions<TokenIssuanceOptions>()
            .BindConfiguration(TokenIssuanceOptions.SectionName)
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0,
                "Iam:Tokens lifetimes must be positive.")
            .ValidateOnStart();

        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthTokenIssuer, AuthTokenIssuer>();

        services.AddDbContextMigration<IamDbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/iam").WithTags("Iam");

        group.MapGet("/ping", () => Results.Ok(new { module = "iam", status = "ok" }))
            .AllowAnonymous();

        group.MapAuthEndpoints();
    }
}
