using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BuildingBlocks.Application.Abstractions.Locking;
using BuildingBlocks.Application.Abstractions.Security;
using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Domain.Security;

namespace BuildingBlocks.Application;

public static class DependencyInjection
{
    // Registers MediatR (in-process request/response messaging) scanning the given assemblies for
    // IRequestHandler<,> implementations, the validation pipeline behavior, and every FluentValidation
    // IValidator<T> found in those same assemblies — so boundary validation runs automatically for any
    // request that has a validator.
    public static IServiceCollection AddApplicationMessaging(this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(handlerAssemblies);
            cfg.AddOpenBehavior(typeof(SensitiveRequestBehavior<,>)); // re-verify caller (if ISensitiveRequest)
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)); // then validate
            cfg.AddOpenBehavior(typeof(LockBehavior<,>)); // then acquire the distributed lock (if ILockRequest)
        });

        services.AddValidatorsFromAssemblies(handlerAssemblies, includeInternalTypes: true);

        // Fail-loud default; the Redis infrastructure registration overrides it with the real lock.
        services.TryAddSingleton<IDistributedLock, UnconfiguredDistributedLock>();
        // Safe defaults so the security behaviors always resolve; the host/Redis registrations override them.
        services.TryAddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddSingleton<ITokenBlacklist, NoOpTokenBlacklist>();
        return services;
    }
}
