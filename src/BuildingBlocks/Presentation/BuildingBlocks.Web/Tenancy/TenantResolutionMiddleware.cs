using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application.Abstractions.Tenancy;
using BuildingBlocks.Domain.Observability;

namespace BuildingBlocks.Web;

/// <summary>
/// Resolves the tenant for the request from the resolver chain (first match wins) and publishes it: into the scoped
/// <see cref="ITenantContext"/> the persistence layer reads, and into <c>Activity</c> baggage as <c>tenant_id</c> so it
/// rides the traces/logs alongside correlation_id. No match leaves the context tenant-less (fail closed downstream).
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AmbientTenantContext tenantContext, IEnumerable<ITenantResolver> resolvers)
    {
        foreach (var resolver in resolvers)
        {
            if (!resolver.TryResolve(context, out var tenantId))
                continue;

            tenantContext.SetResolved(tenantId);
            Activity.Current?.SetBaggage(TelemetryConstants.TenantIdKey, tenantId.ToString());
            break;
        }

        await next(context);
    }
}

public static class TenancyExtensions
{
    /// <summary>Registers the scoped tenant context + the resolver chain (claim → header). Add more resolvers to extend it.</summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<ITenantResolver, ClaimTenantResolver>();
        services.AddSingleton<ITenantResolver, HeaderTenantResolver>();
        return services;
    }

    /// <summary>Runs tenant resolution — place after authentication so the JWT claim is available.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
