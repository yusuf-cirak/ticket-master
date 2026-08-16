using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;
using TicketMaster.Modules.Iam.Application.Features.Authentication.Queries;
using BuildingBlocks.Web;

namespace TicketMaster.Modules.Iam.Host;

/// <summary>
/// Thin authentication endpoints: register / login / refresh / logout / me. Each binds its Mediator command from the
/// request body and maps the returned <c>Result</c> to HTTP via <see cref="ResultEndpointExtensions"/>. All behavior
/// (validation, lockout, rotation, blacklist) lives in the handlers.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapPost("/register", async (RegisterCommand.Command c, ISender sender) =>
        {
            var r = await sender.Send(c);
            return r.IsSuccess ? Results.Created($"/api/iam/users/{r.Value.UserId}", r.Value) : r.ToApiResult();
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginCommand.Command c, ISender sender) => (await sender.Send(c)).ToApiResult())
            .AllowAnonymous();

        group.MapPost("/refresh", async (RefreshCommand.Command c, ISender sender) => (await sender.Send(c)).ToApiResult())
            .AllowAnonymous();

        group.MapPost("/logout", async (LogoutCommand.Command c, ISender sender) =>
        {
            var r = await sender.Send(c);
            return r.IsSuccess ? Results.NoContent() : r.ToApiResult();
        }).RequireAuthorization();

        group.MapGet("/me", async (ISender sender) => (await sender.Send(new GetMeQuery.Query())).ToApiResult())
            .RequireAuthorization();
    }
}
