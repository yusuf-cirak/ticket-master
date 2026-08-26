using Microsoft.AspNetCore.Identity;
using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Auditing;
using TicketMaster.Modules.Iam.Domain.Events;
using DomainEmail = BuildingBlocks.Domain.ValueObjects.Email;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>
/// A user account. Merges the rich aggregate (factory, invariants, domain events) with the ASP.NET Identity user
/// store (password hash, lockout, security stamp) as one entity — Identity is the persistence/authentication backing
/// of the aggregate. Identity roles are NOT used; roles are our own <see cref="Role"/> aggregate. Construct via
/// <see cref="Register"/>. <see cref="IdentityUser{TKey}.NormalizedEmail"/> holds the Gmail-canonical uniqueness key.
/// </summary>
public sealed class User : IdentityUser<UserId>, IAggregateRoot, IAuditable
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly HashSet<RoleId> _roles = [];

    public User() { } // Identity + EF materialization

    private User(UserId id, DomainEmail email, string displayName)
    {
        Id = id;
        UserName = email.Value;
        Email = email.Value;
        NormalizedEmail = email.Canonical.ToUpperInvariant(); // uniqueness key (dot/plus-canonical)
        DisplayName = displayName;
        Status = UserStatus.Active;
    }

    public string DisplayName { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; }

    // ponytail: role assignments are not persisted in this slice (mapped in the authorization slice); the in-memory
    // set keeps the aggregate behavior. Kept out of EF via Ignore.
    public IReadOnlyCollection<RoleId> Roles => _roles;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    public static Result<User> Register(DomainEmail email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Error.Create("user.display_name_required", "Display name is required.", 400);
        if (displayName.Length > 200)
            return Error.Create("user.display_name_too_long", "Display name must be at most 200 characters.", 400);

        var user = new User(UserId.New(), email, displayName.Trim());
        user.Raise(new UserRegisteredDomainEvent(user.Id, email));
        return user;
    }

    public void AssignRole(RoleId roleId) => _roles.Add(roleId);

    public void RemoveRole(RoleId roleId) => _roles.Remove(roleId);

    public Result Lock()
    {
        if (Status == UserStatus.Disabled)
            return Error.Create("user.disabled", "A disabled user cannot be locked.", 409);
        Status = UserStatus.Locked;
        return Result.Success();
    }

    public void Unlock() => Status = UserStatus.Active;

    public void Disable() => Status = UserStatus.Disabled;

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
