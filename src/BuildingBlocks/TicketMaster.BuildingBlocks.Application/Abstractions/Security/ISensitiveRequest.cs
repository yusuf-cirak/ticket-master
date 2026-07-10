namespace TicketMaster.BuildingBlocks.Application.Abstractions.Security;

/// <summary>
/// Marks a security-sensitive command (change password, revoke sessions, delete account, …). The sensitive-request
/// behavior re-verifies the caller at handler time: they must be authenticated and their token must not have been
/// revoked since it was issued — defense-in-depth beyond the one-time check at the HTTP edge, and it also guards
/// commands dispatched through the mediator outside an HTTP request.
/// </summary>
public interface ISensitiveRequest;
