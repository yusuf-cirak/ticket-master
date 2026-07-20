using YC.Monad;

namespace BuildingBlocks.Application.Abstractions.Tenancy;

/// <summary>
/// The ambient tenant for the current scope (request or background job). Resolved once at the edge and read by the
/// persistence layer for stamping writes and scoping reads. An absent value means "no tenant" — a request that touches a
/// tenant-owned entity in that state is rejected by the interceptor rather than leaking across tenants.
/// </summary>
public interface ITenantContext
{
    Option<Guid> TenantId { get; }

    bool HasTenant { get; }

    /// <summary>
    /// Overrides the ambient tenant for the lifetime of the returned scope — for a background job or admin task that
    /// must act as a specific tenant. Disposing restores the previous value. (Cross-tenant <i>reads</i> use EF's
    /// <c>IgnoreQueryFilters()</c>, not this.)
    /// </summary>
    IDisposable BeginScope(Guid tenantId);
}
