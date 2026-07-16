using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Persistence.EfCore.Interceptors;

public static class DomainEventDispatchExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="DomainEventDispatchInterceptor"/>. Add it to a module's DbContext with the
    /// service-provider overload so it resolves from the same scope as the context:
    /// <code>AddDbContext&lt;T&gt;((sp, o) => o.…AddInterceptors(sp.GetRequiredService&lt;DomainEventDispatchInterceptor&gt;()))</code>
    /// </summary>
    public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)
    {
        services.AddScoped<DomainEventDispatchInterceptor>();
        return services;
    }

    /// <summary>
    /// Registers the scoped <see cref="AuditingSaveChangesInterceptor"/> (+ <see cref="TimeProvider"/>). Add it to a
    /// module's DbContext the same way as <see cref="AddDomainEventDispatch"/>.
    /// </summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuditingSaveChangesInterceptor>();
        return services;
    }
}
