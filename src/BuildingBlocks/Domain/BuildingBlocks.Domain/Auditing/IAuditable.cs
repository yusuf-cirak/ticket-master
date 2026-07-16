namespace BuildingBlocks.Domain.Auditing;

/// <summary>
/// Marks an entity whose create/modify metadata is stamped automatically by the auditing SaveChanges interceptor
/// (from <c>ICurrentUser</c> + <c>TimeProvider</c>). Setters are present for the interceptor; application code should
/// treat these as read-only.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; set; }
    string CreatedBy { get; set; }
    DateTimeOffset? ModifiedAtUtc { get; set; }
    string ModifiedBy { get; set; }
}
