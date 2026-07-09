using TicketMaster.BuildingBlocks.Domain;
using TicketMaster.BuildingBlocks.Domain.Auditing;
using TicketMaster.BuildingBlocks.Domain.ValueObjects;
using TicketMaster.Modules.Iam.Domain.Events;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>
/// A user account (rich aggregate). ASP.NET Identity persists the auth store (password hash, lockout) separately;
/// this aggregate owns the business identity, invariants and domain events. Construct via <see cref="Register"/>.
/// Roles are referenced by id; effective permissions are resolved (and cached) at authorization time, not stored here.
/// </summary>
public sealed class User : AggregateRoot<UserId>, IAuditable
{
    private readonly HashSet<RoleId> _roles = [];

    private User() // EF
    {
        Email = null!;
        DisplayName = null!;
    }

    private User(UserId id, Email email, string displayName) : base(id)
    {
        Email = email;
        DisplayName = displayName;
        Status = UserStatus.Active;
    }

    public Email Email { get; private set; }
    public string DisplayName { get; private set; }
    public UserStatus Status { get; private set; }
    public IReadOnlyCollection<RoleId> Roles => _roles;

    // Auditing (stamped by the interceptor).
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public static Result<User> Register(Email email, string displayName)
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
}
