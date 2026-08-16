using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BuildingBlocks.Application.Abstractions.Locking;
using BuildingBlocks.Application.Abstractions.Security;
namespace BuildingBlocks.Application;

public static class DependencyInjection
{
    // Registers every FluentValidation IValidator<T> found in the given assemblies — so boundary validation runs
    // automatically for any request that has a validator, via ValidationBehavior.
    // Mediator itself (the request/response dispatch + the SensitiveRequest/Validation/Lock pipeline) is registered
    // separately in TicketMaster.AppHost/Program.cs: Mediator.SourceGenerator only generates AddMediator() in the
    // outermost project, so it can't be called from this library.
    public static IServiceCollection AddApplicationValidation(this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        services.AddValidatorsFromAssemblies(handlerAssemblies, includeInternalTypes: true);

        // Fail-loud default; the Redis infrastructure registration overrides it with the real lock.
        services.TryAddSingleton<IDistributedLock, UnconfiguredDistributedLock>();
        // Safe defaults so the security behaviors always resolve; the host/Redis registrations override them.
        services.TryAddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddSingleton<ITokenBlacklist, NoOpTokenBlacklist>();
        return services;
    }
}
