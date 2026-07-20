using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

/// <summary>Where the tenant is read from, in resolver precedence order.</summary>
public static class TenancyConstants
{
    /// <summary>JWT claim carrying the tenant id (populated once users are tenant-scoped).</summary>
    public const string TenantClaim = "tenant_id";

    /// <summary>Explicit header, for service-to-service or clients that aren't JWT-bound.</summary>
    public const string TenantHeader = "X-Tenant-Id";
}

/// <summary>
/// One way of reading the tenant from the request. Resolvers are tried in registration order; the first to return a
/// value wins. Add an <see cref="ITenantResolver"/> (e.g. a subdomain resolver backed by a tenant registry) to extend
/// the chain — nothing else changes.
/// </summary>
public interface ITenantResolver
{
    bool TryResolve(HttpContext context, out Guid tenantId);
}

/// <summary>Reads the tenant from the authenticated principal's <see cref="TenancyConstants.TenantClaim"/> claim.</summary>
public sealed class ClaimTenantResolver : ITenantResolver
{
    public bool TryResolve(HttpContext context, out Guid tenantId)
    {
        tenantId = default;
        var value = context.User.FindFirst(TenancyConstants.TenantClaim)?.Value;
        return !string.IsNullOrEmpty(value) && Guid.TryParse(value, out tenantId);
    }
}

/// <summary>Reads the tenant from the <see cref="TenancyConstants.TenantHeader"/> request header.</summary>
public sealed class HeaderTenantResolver : ITenantResolver
{
    public bool TryResolve(HttpContext context, out Guid tenantId)
    {
        tenantId = default;
        return context.Request.Headers.TryGetValue(TenancyConstants.TenantHeader, out var value)
               && Guid.TryParse(value, out tenantId);
    }
}
