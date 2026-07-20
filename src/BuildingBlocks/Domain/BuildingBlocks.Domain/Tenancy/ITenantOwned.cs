namespace BuildingBlocks.Domain.Tenancy;

/// <summary>
/// Marks an aggregate/entity as owned by a tenant. The <see cref="TenantId"/> is a discriminator the persistence layer
/// manages end-to-end: stamped on insert by the tenancy interceptor, enforced on every read by a global query filter,
/// and guarded against cross-tenant modification. Application/domain code never sets or filters it by hand.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }

    /// <summary>Called only by the persistence layer (insert-time stamping); not part of the domain surface.</summary>
    void SetTenantId(Guid tenantId);
}
