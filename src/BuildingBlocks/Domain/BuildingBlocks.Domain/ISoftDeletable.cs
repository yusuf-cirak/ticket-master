namespace BuildingBlocks.Domain;

/// <summary>
/// Marks an entity that is soft-deleted rather than physically removed: a delete flips <see cref="IsDeleted"/> (the
/// soft-delete interceptor rewrites the delete to an update and stamps who/when), and a global query filter hides
/// soft-deleted rows from normal queries. Setters exist for the interceptor; treat as read-only in application code.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAtUtc { get; set; }
    string DeletedBy { get; set; }
}
