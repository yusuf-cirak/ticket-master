using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Application.Behaviors;

namespace TicketMaster.BuildingBlocks.Application;

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
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)); // runs before handlers
        });

        services.AddValidatorsFromAssemblies(handlerAssemblies, includeInternalTypes: true);
        return services;
    }
}
