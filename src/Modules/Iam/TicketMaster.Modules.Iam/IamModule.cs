using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.ServiceDefaults.Modularity;

namespace TicketMaster.Modules.Iam;

/// <summary>
/// Identity &amp; Access Management module. Currently a walking skeleton that proves the module-discovery seam with a
/// second module; the domain (users/roles), Identity store, JWT issuer, Keycloak federation and permission
/// authorization are added in subsequent slices.
/// </summary>
public sealed class IamModule : IModule
{
    public string Name => "Iam";

    public Assembly Assembly => typeof(IamModule).Assembly;

    public void Add(IServiceCollection services, IConfiguration configuration)
    {
        // Slices to come: AddIdentityCore + IamDbContext, JWT issuer, Keycloak sync, permission policies.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/iam").WithTags("Iam");

        group.MapGet("/ping", () => Results.Ok(new { module = "iam", status = "ok" }))
            .AllowAnonymous();
    }
}
