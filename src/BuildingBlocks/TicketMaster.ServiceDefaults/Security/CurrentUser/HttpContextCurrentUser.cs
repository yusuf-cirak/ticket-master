using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Domain.Security;
using YC.Monad;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the ambient <see cref="HttpContext"/> principal. Reads the hardened claim
/// layout produced by <c>AddJwtAuthentication</c> (<c>sub</c> unmapped as the subject; <c>role</c> for roles;
/// <c>permission</c> for fine-grained permissions).
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const string PermissionClaimType = "permission";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Option<string> UserId => Option.Create(Principal?.FindFirstValue("sub"));

    public Option<string> UserName =>
        Option.Create(Principal?.FindFirstValue("name") ?? Principal?.FindFirstValue("preferred_username"));

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll("role").Select(c => c.Value).ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(PermissionClaimType).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(PermissionClaimType, permission) ?? false;
}

public static class CurrentUserExtensions
{
    /// <summary>Registers <see cref="ICurrentUser"/> (scoped) over <c>IHttpContextAccessor</c>.</summary>
    public static WebApplicationBuilder AddCurrentUser(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        return builder;
    }
}
