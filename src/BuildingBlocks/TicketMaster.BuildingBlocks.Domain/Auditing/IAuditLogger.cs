namespace TicketMaster.BuildingBlocks.Domain.Auditing;

/// <summary>
/// Append-only audit trail for security-relevant actions (login, role/permission change, sensitive mutations) —
/// the "who did what, when" record, distinct from entity create/modify stamps (<see cref="IAuditable"/>) and from
/// domain events. The actor and correlation context are taken from the ambient <c>ICurrentUser</c>/telemetry, so
/// callers pass only the action and its subject.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default);
}
