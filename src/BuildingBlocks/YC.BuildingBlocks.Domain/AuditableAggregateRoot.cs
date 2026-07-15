using YC.BuildingBlocks.Domain.Auditing;

namespace YC.BuildingBlocks.Domain;

/// <summary>
/// An <see cref="AggregateRoot{TId}"/> that also carries the create/modify audit stamps and soft-delete state, so a
/// typical aggregate gets domain events + auditing + soft delete from one base (the interceptors fill these in).
/// Aggregates that need none of this can still derive from <see cref="AggregateRoot{TId}"/> directly.
/// </summary>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable, ISoftDeletable
    where TId : notnull
{
    protected AuditableAggregateRoot(TId id) : base(id) { }
    protected AuditableAggregateRoot() { }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public string DeletedBy { get; set; } = string.Empty;
}
